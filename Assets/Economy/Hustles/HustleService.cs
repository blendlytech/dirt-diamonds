using System;
using System.Collections.Generic;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.Simulation.Hustles;
using DirtAndDiamonds.Simulation.Life;

namespace DirtAndDiamonds.Economy.Hustles;

/// <summary>
/// Which Work-block activity the avatar has selected for the planned day
/// (docs/design/hustles_narcotics_fencing.md §2) — a GameManager-owned intent,
/// mirroring how it already owns the daily-schedule bridge. LegalWork is the
/// 8a passive payout (default); Narcotics/Fencing/Poker each arm an
/// interactive <see cref="PendingHustleSession"/> once that day's Work block
/// runs. Poker's arming/forfeit needs no GameManager changes at all — every
/// call site below already branches on "any non-LegalWork activity", so this
/// one enum member is the entire daily-clock integration (docs/design/hustles_texas_holdem.md §2).
/// </summary>
public enum WorkActivity : byte
{
    LegalWork,
    Narcotics,
    Fencing,
    Poker,
    Robbery,
}

/// <summary>One armed-but-unplayed interactive hustle session, awaiting the player (§2's Game-block mirror).</summary>
public readonly struct PendingHustleSession
{
    public readonly WorkActivity Activity;
    public readonly long Day;

    public PendingHustleSession(WorkActivity activity, long day)
    {
        Activity = activity;
        Day = day;
    }
}

/// <summary>
/// Layer 2 orchestration (§1): builds the <see cref="HustleContext"/>/
/// <see cref="FencingContext"/> snapshots the pure Layer 1 resolvers consume,
/// and applies a resolved <see cref="HustleResolution"/> through the same
/// primitive writers gritty events use — DB writes in this service's own
/// batch, then bus impulses and in-memory RelationshipGraph writes, mirroring
/// <see cref="Narrative.Events.EventConsequenceApplier"/>'s application
/// discipline exactly (§5). Never touches <see cref="Narrative.Events.ConsequenceKind"/>
/// directly — hustle resolutions aren't authored JSON content, so this is the
/// disclosed fallback (§5's "parallel HustleService apply-loop over the same
/// writers") rather than the full shared-ConsequenceApplier refactor; the two
/// risk writers were still added to the gritty-event vocabulary too, so 8c
/// inherits a tested type regardless of which path a future event uses.
///
/// Faction reps (§6: one supplier, one local crew, plus a "fence" for Fencing
/// — filling a gap the design doc's own faction list left implicit for
/// fenceStanding, §4.1) are chosen once from the existing player pool and
/// cached as Game_State pointers (<see cref="GameStateKeys.HustleSupplierPlayerId"/>
/// etc.) — cheaper than a reverse Entity_Flags scan on every session, while
/// still tagging the chosen NPC with the narrative-legible flag the design
/// doc names.
/// </summary>
public sealed class HustleService
{
    private readonly DatabaseManager _db;
    private readonly PlayerQueries _players;
    private readonly GameStateQueries _gameState;
    private readonly RelationshipGraph _relationships;
    private readonly EventBus _bus;
    private RngState _rng;

    // Reused for the rare (at most 3 times ever per save) faction-rep pick —
    // not a hot path, so a one-time bulk load is fine (EventConsequenceApplier's
    // own target-pool precedent).
    private readonly List<PlayerRow> _playerScratch = new();

    public HustleService(
        DatabaseManager db, PlayerQueries players, GameStateQueries gameState,
        RelationshipGraph relationships, EventBus bus, ulong rngSeed)
    {
        _db = db;
        _players = players;
        _gameState = gameState;
        _relationships = relationships;
        _bus = bus;
        _rng = new RngState(rngSeed | 1UL);
    }

    // ------------------------------------------------------------------
    // Context builders (Layer 1 inputs)
    // ------------------------------------------------------------------

    /// <summary>Snapshots §3.0's context for <paramref name="playerId"/> — the avatar in practice, but generic over any subject id.</summary>
    public HustleContext BuildNarcoticsContext(string playerId, long day)
    {
        PlayerRow row = RequirePlayer(playerId);
        string supplierId = ResolveFactionRep(GameStateKeys.HustleSupplierPlayerId, "faction_supplier", playerId, day);
        string crewId = ResolveFactionRep(GameStateKeys.HustleCrewPlayerId, "faction_crew_1", playerId, day);

        _relationships.TryGetRelationship(playerId, supplierId, out int supplierTrust, out _);
        _relationships.TryGetRelationship(playerId, crewId, out int crewStanding, out _);

        var flags = new List<EntityFlagRow>();
        _players.LoadActiveFlags(playerId, flags);
        bool usesProduct = false;
        bool ownsTurf = false;
        foreach (EntityFlagRow flag in flags)
        {
            if (string.Equals(flag.FlagName, "uses_product", StringComparison.Ordinal))
            {
                usesProduct = true;
            }
            else if (string.Equals(flag.FlagName, "controls_turf_local", StringComparison.Ordinal))
            {
                ownsTurf = true;
            }
        }

        return new HustleContext(
            row.Funds, row.DetectionRisk / 100.0, row.Recklessness / 100.0,
            supplierTrust, crewStanding, ownsTurf, usesProduct);
    }

    /// <summary>Snapshots §4.1's context for <paramref name="playerId"/>.</summary>
    public FencingContext BuildFencingContext(string playerId, long day)
    {
        PlayerRow row = RequirePlayer(playerId);
        string fenceId = ResolveFactionRep(GameStateKeys.HustleFencePlayerId, "faction_fence", playerId, day);
        _relationships.TryGetRelationship(playerId, fenceId, out int fenceStanding, out _);
        return new FencingContext(row.DetectionRisk / 100.0, fenceStanding);
    }

    /// <summary>Snapshots docs/design/hustles_texas_holdem.md §9's context for <paramref name="playerId"/> — simpler than Narcotics'/Fencing's: no faction reps at all, so no graph/Game_State touch.</summary>
    public HoldemContext BuildHoldemContext(string playerId)
    {
        PlayerRow row = RequirePlayer(playerId);
        return new HoldemContext(row.Funds, row.DetectionRisk / 100.0, row.Recklessness / 100.0);
    }

    /// <summary>
    /// Snapshots docs/design/hustle_minigames_depth_pass.md §5's context for
    /// <paramref name="playerId"/>. <see cref="RobberyContext.HasCrew"/> reuses
    /// the same <see cref="GameStateKeys.HustleCrewPlayerId"/> rep Narcotics
    /// resolves (§5.1.2) — <see cref="ResolveFactionRep"/> always resolves (and
    /// caches) a rep given a large-enough player pool, so <c>HasCrew</c> is true
    /// whenever that resolution succeeds; a too-small world throws there exactly
    /// as it does for Narcotics/Fencing today, so Robbery degrades identically.
    /// </summary>
    public RobberyContext BuildRobberyContext(string playerId, long day)
    {
        PlayerRow row = RequirePlayer(playerId);
        ResolveFactionRep(GameStateKeys.HustleCrewPlayerId, "faction_crew_1", playerId, day);
        return new RobberyContext(row.Funds, row.DetectionRisk / 100.0, row.Recklessness / 100.0, hasCrew: true);
    }

    // ------------------------------------------------------------------
    // Resolution application (§5)
    // ------------------------------------------------------------------

    /// <summary>
    /// Applies a resolved Narcotics run: the shared primitives via
    /// <see cref="ApplyCore"/>, plus the two faction-specific deltas
    /// <see cref="HustleResolution"/> only Narcotics ever populates.
    /// </summary>
    public void ApplyNarcoticsResolution(string playerId, in HustleResolution resolution, long day)
    {
        ApplyCore(playerId, in resolution, day);

        if (resolution.SupplierTrustDelta != 0)
        {
            string supplierId = RequireResolvedRep(GameStateKeys.HustleSupplierPlayerId, "supplier");
            AdjustOrCreateRelationship(playerId, supplierId, resolution.SupplierTrustDelta, RelationshipKind.Friend);
        }

        if (resolution.CrewStandingDelta != 0 || resolution.SetControlsTurfFlag)
        {
            string crewId = RequireResolvedRep(GameStateKeys.HustleCrewPlayerId, "crew");
            if (resolution.CrewStandingDelta != 0)
            {
                // Any negative interaction with the crew reads as adversarial in
                // this narrow model — there is no "friendly crew" branch (§3.3).
                AdjustOrCreateRelationship(playerId, crewId, resolution.CrewStandingDelta, RelationshipKind.Rival);
            }
            if (resolution.SetControlsTurfFlag)
            {
                _players.SetFlag(playerId, "controls_turf_local", true, day);
            }
        }
    }

    /// <summary>Applies a resolved Fencing negotiation — the shared primitives only; Fencing never touches the faction graph.</summary>
    public void ApplyFencingResolution(string playerId, in HustleResolution resolution, long day) =>
        ApplyCore(playerId, in resolution, day);

    /// <summary>Applies a resolved Hold'em session — the shared primitives only; Hold'em never touches the faction graph (§9/§11).</summary>
    public void ApplyHoldemResolution(string playerId, in HustleResolution resolution, long day) =>
        ApplyCore(playerId, in resolution, day);

    /// <summary>
    /// Applies a resolved Robbery run: the shared primitives via
    /// <see cref="ApplyCore"/> (funds/detection/health/reckless/stress and the
    /// additive <c>robbery_bust</c> flag), plus the crew-job
    /// <see cref="HustleResolution.CrewStandingDelta"/> — the same adversarial
    /// crew-edge write Narcotics' <c>PushTerritory</c> crew path uses (§5.1.2:
    /// a crew job risks the split *and* the relationship).
    /// </summary>
    public void ApplyRobberyResolution(string playerId, in HustleResolution resolution, long day)
    {
        ApplyCore(playerId, in resolution, day);

        if (resolution.CrewStandingDelta != 0)
        {
            string crewId = RequireResolvedRep(GameStateKeys.HustleCrewPlayerId, "crew");
            AdjustOrCreateRelationship(playerId, crewId, resolution.CrewStandingDelta, RelationshipKind.Rival);
        }
    }

    private void ApplyCore(string playerId, in HustleResolution resolution, long day)
    {
        _db.BeginBatch();
        try
        {
            if (resolution.FundsDelta != 0)
            {
                _players.AdjustFunds(playerId, resolution.FundsDelta);
            }
            if (resolution.DetectionRiskDelta != 0)
            {
                _players.AdjustDetectionRisk(playerId, resolution.DetectionRiskDelta);
            }
            if (resolution.HealthCeilingDelta != 0)
            {
                _players.AdjustHealthCeiling(playerId, resolution.HealthCeilingDelta);
            }
            if (resolution.RecklessnessDelta != 0)
            {
                _players.AdjustRecklessness(playerId, resolution.RecklessnessDelta);
            }
            if (resolution.SetWatchlistFlag)
            {
                _players.SetFlag(playerId, "narc_watchlist", true, day);
            }
            if (resolution.SetBadProductFlag)
            {
                _players.SetFlag(playerId, "bad_product", true, day);
            }
            if (resolution.SetSpoiledGoodsFlag)
            {
                _players.SetFlag(playerId, "spoiled_goods", true, day);
            }
            if (resolution.SetGamblingBustFlag)
            {
                _players.SetFlag(playerId, "gambling_bust", true, day);
            }
            if (resolution.SetRobberyBustFlag)
            {
                _players.SetFlag(playerId, "robbery_bust", true, day);
            }
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }

        // Bus impulses follow the committed batch, so a subscriber reacting to
        // one always observes the DB state this resolution just produced —
        // the same ordering EventConsequenceApplier keeps.
        if (resolution.FundsDelta != 0)
        {
            _bus.Publish(new FundsImpulseEvent(playerId, resolution.FundsDelta));
        }
        if (resolution.StressDelta != 0)
        {
            _bus.Publish(new StressImpulseEvent(playerId, (float)resolution.StressDelta));
        }
    }

    private void AdjustOrCreateRelationship(string subjectId, string targetId, int delta, RelationshipKind defaultKindIfNew)
    {
        if (_relationships.TryGetRelationship(subjectId, targetId, out _, out _))
        {
            _relationships.AdjustAffinity(subjectId, targetId, delta);
        }
        else
        {
            _relationships.SetRelationship(subjectId, targetId, delta, defaultKindIfNew);
        }
    }

    // ------------------------------------------------------------------
    // Faction rep resolution (§6)
    // ------------------------------------------------------------------

    private static readonly string[] FactionGameStateKeys =
    {
        GameStateKeys.HustleSupplierPlayerId, GameStateKeys.HustleCrewPlayerId, GameStateKeys.HustleFencePlayerId,
    };

    /// <summary>Resolves (and, on the very first call, picks + tags) the faction rep cached under <paramref name="gameStateKey"/>.</summary>
    private string ResolveFactionRep(string gameStateKey, string flagName, string subjectId, long day)
    {
        if (_gameState.TryGetText(gameStateKey, out string existingId))
        {
            return existingId;
        }

        var excluded = new HashSet<string>(StringComparer.Ordinal) { subjectId };
        foreach (string key in FactionGameStateKeys)
        {
            if (_gameState.TryGetText(key, out string other))
            {
                excluded.Add(other);
            }
        }

        _players.LoadAll(_playerScratch);
        if (_playerScratch.Count <= excluded.Count)
        {
            throw new InvalidOperationException("Not enough players in the world to resolve a new hustle faction rep.");
        }
        string picked;
        do
        {
            picked = _playerScratch[_rng.NextInt(_playerScratch.Count)].PlayerId;
        }
        while (excluded.Contains(picked));

        _db.BeginBatch();
        try
        {
            _gameState.SetText(gameStateKey, picked);
            _players.SetFlag(picked, flagName, true, day);
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }
        return picked;
    }

    private string RequireResolvedRep(string gameStateKey, string label) =>
        _gameState.TryGetText(gameStateKey, out string id)
            ? id
            : throw new InvalidOperationException(
                $"No {label} faction rep resolved yet — BuildNarcoticsContext must run before applying a resolution.");

    private PlayerRow RequirePlayer(string playerId) =>
        _players.TryGetById(playerId, out PlayerRow row)
            ? row
            : throw new InvalidOperationException($"'{playerId}' has no Players row — cannot run a hustle for it.");
}
