using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Narrative.Events;

namespace DirtAndDiamonds.Platform.Steam;

/// <summary>
/// Phase 11b (steam_publishing_ship_it.md §4): achievements as a bus consumer.
/// Structurally another ledger — a pure subscriber over already-published
/// events that holds no authority over sim state and writes nothing to the
/// database; its only side effect is a guarded <see cref="SteamIntegration"/>
/// call, so the wiring runs identically with and without a Steam client (§2.2).
/// Steam owns unlock state (§4.3): every unlock is an idempotent re-assert, so
/// milestone checks re-run on every relevant event (and once at boot via
/// <see cref="SyncFromBoot"/>) with no "already fired?" bit anywhere in the
/// save — PRAGMA user_version stays at 10 because of this, not despite it.
/// Attached at the end of GameManager._Ready, after every publisher, so its
/// SeasonRolledOverEvent reads always observe that same rollover's succession
/// and game-over writes (per-channel handlers run in subscription order).
/// </summary>
public sealed class AchievementManager
{
    // Achievement API names — must match the definitions minted on the
    // Steamworks partner site in 11e. Against the 11a dev appid (Spacewar)
    // none of these exist, so a live-client unlock fails server-side silently:
    // the expected dev state, same posture as no client at all.
    private const string AchWentPro = "ACH_WENT_PRO";
    private const string AchTheShow = "ACH_THE_SHOW";
    private const string AchNextOfKin = "ACH_NEXT_OF_KIN";
    private const string AchDynasty = "ACH_DYNASTY";
    private const string AchEndOfTheLine = "ACH_END_OF_THE_LINE";
    private const string AchRapSheet = "ACH_RAP_SHEET";
    private const string AchMoonlighting = "ACH_MOONLIGHTING";
    private const string AchJuiced = "ACH_JUICED";

    /// <summary>
    /// Journeyman's running total (§4.2) — a Steam Stat, held server-side;
    /// the stat-vs-threshold rule that turns it into the achievement lives on
    /// the partner site, never in code or the local .db.
    /// </summary>
    private const string StatSeasonsPlayed = "STAT_SEASONS_PLAYED";

    /// <summary>The game's own 3-generation exit criterion (GameStateKeys.DynastyGeneration doc).</summary>
    private const long DynastyGenerationTarget = 3;

    /// <summary>The one underworld event reachable WITHOUT prior engagement — only its engaging choice moonlights.</summary>
    private const string BackAlleyBribeEventId = "back_alley_bribe";
    private const string BackAlleyBribeEngagingChoiceId = "take_the_envelope";

    // The §4.2 content-id families. These mirror the shipped batches under
    // Assets/Narrative/Events/Content — a renamed content id must be renamed
    // here too (check_event_graph_integrity does not know about this mapping).
    // Underworld: the syndicate cascade plus the narcotics arrest, every one of
    // which (bar the bribe itself, see BackAlleyBribeEventId) only ever fires
    // because the subject already engaged — the prerequisites key on
    // compromised_syndicate / syndicate_marked / narc_watchlist.
    private static readonly HashSet<string> UnderworldEventIds = new(StringComparer.Ordinal)
    {
        BackAlleyBribeEventId,
        "syndicate_shakedown",
        "syndicate_enforcers",
        "narcotics_arrest",
    };

    // PED: the detection_risk arc — the public scandal plus the three
    // league-test suspension tiers.
    private static readonly HashSet<string> PedEventIds = new(StringComparer.Ordinal)
    {
        "caught_juicing",
        "suspended_ped_test_flag",
        "suspended_repeat_violation",
        "suspended_lifetime_watch",
    };

    private readonly SteamIntegration _steam;
    private readonly BaseballQueries _baseball;
    private readonly GameStateQueries _gameState;
    private readonly GrittyEventLibrary _library;

    private readonly Action<AvatarChangedEvent> _onAvatarChanged;
    private readonly Action<SeasonRolledOverEvent> _onSeasonRolledOver;
    private readonly Action<ChildBornEvent> _onChildBorn;
    private readonly Action<PlayerAbsenceChangedEvent> _onAbsenceChanged;
    private readonly Action<GrittyEventResolvedEvent> _onGrittyEventResolved;

    // Gritty events, absences, and season ticks fire for NPCs too — every
    // subject-carrying signal filters on the live avatar. Null until the first
    // AvatarChangedEvent (or the boot sync) installs one.
    private string? _avatarId;

    public AchievementManager(
        SteamIntegration steam,
        BaseballQueries baseball,
        GameStateQueries gameState,
        GrittyEventLibrary library)
    {
        _steam = steam;
        _baseball = baseball;
        _gameState = gameState;
        _library = library;
        _onAvatarChanged = OnAvatarChanged;
        _onSeasonRolledOver = OnSeasonRolledOver;
        _onChildBorn = OnChildBorn;
        _onAbsenceChanged = OnAbsenceChanged;
        _onGrittyEventResolved = OnGrittyEventResolved;
    }

    public void AttachTo(EventBus bus)
    {
        bus.Subscribe(_onAvatarChanged);
        bus.Subscribe(_onSeasonRolledOver);
        bus.Subscribe(_onChildBorn);
        bus.Subscribe(_onAbsenceChanged);
        bus.Subscribe(_onGrittyEventResolved);
    }

    public void DetachFrom(EventBus bus)
    {
        bus.Unsubscribe(_onAvatarChanged);
        bus.Unsubscribe(_onSeasonRolledOver);
        bus.Unsubscribe(_onChildBorn);
        bus.Unsubscribe(_onAbsenceChanged);
        bus.Unsubscribe(_onGrittyEventResolved);
    }

    /// <summary>
    /// The boot-time counterpart of <see cref="OnAvatarChanged"/>: a loaded
    /// avatar activated before the bus attached (CareerManager's
    /// LoadExistingAvatar publishes nothing), so GameManager syncs it directly —
    /// the SyncLifeSimAvatar precedent. Re-asserting the persisted-state
    /// milestones here is exactly the §4.3 reload case: a save already in MLB
    /// or at generation 3 re-sets an already-set flag, a harmless no-op, and it
    /// closes the gap for saves that earned a milestone before 11b existed (or
    /// while Steam was down).
    /// </summary>
    public void SyncFromBoot(string avatarId, int teamId)
    {
        _avatarId = avatarId;
        EvaluateTier(teamId);
        EvaluateMilestones();
    }

    /// <summary>
    /// Creation, succession, and the 9c promotion republish all land here.
    /// Succession commits DynastyGeneration before ActivateAvatar publishes
    /// (CareerManager.Succeed), so the milestone read below is never stale —
    /// including the interactive heir-choice path, which resolves outside any
    /// season rollover.
    /// </summary>
    private void OnAvatarChanged(AvatarChangedEvent e)
    {
        _avatarId = e.AvatarPlayerId;
        EvaluateTier(e.TeamId);
        EvaluateMilestones();
    }

    private void OnSeasonRolledOver(SeasonRolledOverEvent _)
    {
        // CareerManager's rollover handler (attached earlier, same channel) has
        // already run its succession check — a lineage that just ended is
        // visible to this read within the same dispatch.
        EvaluateMilestones();
        // Journeyman: a season only counts as played while a bloodline holds a
        // career — the pre-creation (or post-game-over) world ticking over is
        // not a season the player journeyed through.
        if (_avatarId is not null)
        {
            _steam.TryAddStat(StatSeasonsPlayed, 1);
        }
    }

    /// <summary>Conception is avatar-only (CareerManager services the bus request), so no subject filter is needed.</summary>
    private void OnChildBorn(ChildBornEvent _) => _steam.TrySetAchievement(AchNextOfKin);

    private void OnAbsenceChanged(PlayerAbsenceChangedEvent e)
    {
        if (e.Reason == (byte)AbsenceReason.Arrest && e.PlayerId == _avatarId)
        {
            _steam.TrySetAchievement(AchRapSheet);
        }
    }

    private void OnGrittyEventResolved(GrittyEventResolvedEvent e)
    {
        if (e.SubjectPlayerId != _avatarId)
        {
            return;
        }
        if (PedEventIds.Contains(e.EventId))
        {
            _steam.TrySetAchievement(AchJuiced);
        }
        if (UnderworldEventIds.Contains(e.EventId) && IsEngagingResolution(in e))
        {
            _steam.TrySetAchievement(AchMoonlighting);
        }
    }

    /// <summary>
    /// Walking away from the bribe must not moonlight. The resolved event
    /// carries only a choice index, so the choice's stable content id is
    /// resolved back through the library — index positions are authoring
    /// order, not contract. Every other underworld event is engagement by
    /// construction (its prerequisites require a prior engaging choice or the
    /// hustle-written watchlist flag), whatever choice resolves it.
    /// </summary>
    private bool IsEngagingResolution(in GrittyEventResolvedEvent e)
    {
        if (!string.Equals(e.EventId, BackAlleyBribeEventId, StringComparison.Ordinal))
        {
            return true;
        }
        return _library.TryGetById(e.EventId, out GrittyEventDefinition definition)
            && (uint)e.ChoiceIndex < (uint)definition.Choices.Length
            && string.Equals(
                definition.Choices[e.ChoiceIndex].Id, BackAlleyBribeEngagingChoiceId, StringComparison.Ordinal);
    }

    private void EvaluateTier(int teamId)
    {
        if (!_baseball.TryGetTeamTier(teamId, out LeagueTier tier))
        {
            return;
        }
        // Went Pro covers every professional rung; The Show is the MLB rung —
        // both re-assertable, so a promotion republish or a reload just re-sets.
        if (tier >= LeagueTier.MinorA)
        {
            _steam.TrySetAchievement(AchWentPro);
        }
        if (tier == LeagueTier.MLB)
        {
            _steam.TrySetAchievement(AchTheShow);
        }
    }

    /// <summary>
    /// The persisted-state milestones (§4.3): both re-evaluated from Game_State
    /// on every relevant event, because Steam is the durable store of what has
    /// fired — two indexed key reads on rare events, nothing hot.
    /// </summary>
    private void EvaluateMilestones()
    {
        if (_gameState.TryGetInt64(GameStateKeys.DynastyGeneration, out long generation)
            && generation >= DynastyGenerationTarget)
        {
            _steam.TrySetAchievement(AchDynasty);
        }
        if (_gameState.TryGetText(GameStateKeys.LineageOverReason, out _))
        {
            _steam.TrySetAchievement(AchEndOfTheLine);
        }
    }
}
