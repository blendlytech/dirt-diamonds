using System.Globalization;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Platform.Steam;

/// <summary>
/// Phase 11d (steam_publishing_ship_it.md §6): rich presence as a bus consumer —
/// the same shape as <see cref="AchievementManager"/>: a pure subscriber over
/// already-published events whose only side effect is a guarded
/// <see cref="SteamIntegration"/> call, so the wiring runs identically with and
/// without a Steam client (§2.2). Presence is a read over already-live state
/// (season year, avatar tier, avatar name/age) — no new data, no writes.
/// Event-driven only: it fires on AvatarChangedEvent (creation, succession, the
/// 9c promotion republish) and SeasonRolledOverEvent (year tick, yearly aging),
/// never from a per-frame path (the ui_conventions no-per-frame-formatting rule
/// generalizes to presence). Attached at the end of GameManager._Ready, after
/// every publisher, so its rollover reads observe that same rollover's
/// succession/game-over writes (per-channel handlers run in subscription order).
/// </summary>
public sealed class RichPresenceWriter
{
    // No English display strings in code (the Phase-10 localization posture —
    // display strings live on the partner site): the game sets stable tokens
    // and raw values; the partner-site rich-presence localization file owns
    // the wording. Tokens are minted verbatim at 11e (the ACH_* precedent) and
    // recorded in Assets/Platform/Steam/README.md; against the 11a dev appid
    // (Spacewar) they render nothing, the expected dev state.
    private const string SteamDisplayKey = "steam_display";
    private const string StatusCareerToken = "#Status_Career";
    private const string StatusBetweenCareersToken = "#Status_BetweenCareers";

    // Substitution keys the #Status_Career token references (%season% etc.).
    private const string SeasonKey = "season";
    private const string TierKey = "tier";
    private const string PlayerKey = "player";
    private const string AgeKey = "age";

    // Tier values are themselves localization tokens (Steam localizes a value
    // that starts with '#'), so tier names localize on the partner site too.
    // Pinned as consts, not derived from LeagueTier.ToString(): an enum rename
    // must never silently change the wire token.
    private const string TierTokenHS = "#Tier_HS";
    private const string TierTokenCollege = "#Tier_College";
    private const string TierTokenMinorA = "#Tier_MinorA";
    private const string TierTokenMinorAA = "#Tier_MinorAA";
    private const string TierTokenMinorAAA = "#Tier_MinorAAA";
    private const string TierTokenMLB = "#Tier_MLB";

    private readonly SteamIntegration _steam;
    private readonly BaseballQueries _baseball;
    private readonly PlayerQueries _players;
    private readonly GameStateQueries _gameState;
    private readonly GlobalState _state;

    private readonly Action<AvatarChangedEvent> _onAvatarChanged;
    private readonly Action<SeasonRolledOverEvent> _onSeasonRolledOver;

    // Season ticks fire for an avatar-less world too; null = between careers
    // (pre-creation or lineage over), under which rollovers publish nothing new.
    private string? _avatarId;
    private int _avatarTeamId;

    public RichPresenceWriter(
        SteamIntegration steam,
        BaseballQueries baseball,
        PlayerQueries players,
        GameStateQueries gameState,
        GlobalState state)
    {
        _steam = steam;
        _baseball = baseball;
        _players = players;
        _gameState = gameState;
        _state = state;
        _onAvatarChanged = OnAvatarChanged;
        _onSeasonRolledOver = OnSeasonRolledOver;
    }

    public void AttachTo(EventBus bus)
    {
        bus.Subscribe(_onAvatarChanged);
        bus.Subscribe(_onSeasonRolledOver);
    }

    public void DetachFrom(EventBus bus)
    {
        bus.Unsubscribe(_onAvatarChanged);
        bus.Unsubscribe(_onSeasonRolledOver);
    }

    /// <summary>
    /// The boot-time counterpart of <see cref="OnAvatarChanged"/> (the
    /// SyncFromBoot precedent: a loaded avatar activated before the bus
    /// attached, so GameManager syncs it directly). A save whose lineage
    /// already ended still carries the parked retiree's avatar pointer — that
    /// is a between-careers session, not a live career.
    /// </summary>
    public void SyncFromBoot(string avatarId, int teamId)
    {
        if (LineageIsOver())
        {
            SetBetweenCareers();
            return;
        }
        _avatarId = avatarId;
        _avatarTeamId = teamId;
        PublishCareerPresence();
    }

    /// <summary>
    /// The no-career status — a fresh save before the founder exists, or a
    /// bloodline that ended. GameManager calls this on an avatar-less boot;
    /// the game-over rollover routes here too.
    /// </summary>
    public void SetBetweenCareers()
    {
        _avatarId = null;
        _steam.SetRichPresence(SteamDisplayKey, StatusBetweenCareersToken);
    }

    /// <summary>Creation, succession, and the 9c promotion republish all land here — every one is an active career.</summary>
    private void OnAvatarChanged(AvatarChangedEvent e)
    {
        _avatarId = e.AvatarPlayerId;
        _avatarTeamId = e.TeamId;
        PublishCareerPresence();
    }

    private void OnSeasonRolledOver(SeasonRolledOverEvent _)
    {
        if (_avatarId is null)
        {
            return;
        }
        // CareerManager's rollover handling (attached earlier, same channel)
        // has already run aging and the succession check — a game-over this
        // rollover is visible to this read, and a succession's heir already
        // arrived via OnAvatarChanged within the same dispatch.
        if (LineageIsOver())
        {
            SetBetweenCareers();
            return;
        }
        PublishCareerPresence();
    }

    private bool LineageIsOver() => _gameState.TryGetText(GameStateKeys.LineageOverReason, out _);

    private void PublishCareerPresence()
    {
        // GlobalState mirrors what TimeManager just committed (events publish
        // post-commit), so the year read is current on every path, boot included.
        _steam.SetRichPresence(SeasonKey, _state.SeasonYear.ToString(CultureInfo.InvariantCulture));
        if (_baseball.TryGetTeamTier(_avatarTeamId, out LeagueTier tier))
        {
            _steam.SetRichPresence(TierKey, TierToken(tier));
        }
        // Re-read on every publish: succession changes the name, the yearly
        // aging tick (which ran before this rollover handler) changes the age.
        // Rare events, so the row read + strings are nothing hot.
        if (_players.TryGetById(_avatarId!, out PlayerRow row))
        {
            _steam.SetRichPresence(PlayerKey, row.FirstName + " " + row.LastName);
            _steam.SetRichPresence(AgeKey, row.Age.ToString(CultureInfo.InvariantCulture));
        }
        _steam.SetRichPresence(SteamDisplayKey, StatusCareerToken);
    }

    private static string TierToken(LeagueTier tier) => tier switch
    {
        LeagueTier.HS => TierTokenHS,
        LeagueTier.College => TierTokenCollege,
        LeagueTier.MinorA => TierTokenMinorA,
        LeagueTier.MinorAA => TierTokenMinorAA,
        LeagueTier.MinorAAA => TierTokenMinorAAA,
        _ => TierTokenMLB,
    };
}
