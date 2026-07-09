using System;
using System.Collections.Generic;
using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.Simulation.Life;

namespace DirtAndDiamonds.Narrative.Social;

/// <summary>
/// One NPC's social identity for the weekly autonomy tick — the same
/// plain-record decoupling NpcSeed gives LifeSimManager: the caller
/// (GameManager) projects Players/Player_Person rows into these, so the
/// service itself never touches the Data layer.
/// </summary>
public readonly record struct SocialSeed(string PlayerId, int? TeamId, int Charisma, int Teamwork);

/// <summary>
/// The HS-5 NPC autonomy tick (docs/design/high_school_person_layer.md §8):
/// NPCs form friendships, rivalries, and romances without the player, so the
/// dating layer's "the rival's ex" is a person who actually exists. Weekly
/// (the family-tick/CoL beat), deterministic on its own <see cref="RngState"/>
/// fork (the schema-v4 Split precedent — it can never perturb the sims'
/// calibrated draw order), and hard-budgeted: at most
/// <see cref="NpcAutonomyProfile.MaxPairInteractionsPerWeek"/> pair
/// interactions regardless of population, so 800+ NPCs can never O(N²).
///
/// Homed in Narrative (not Simulation/Life) deliberately: the service needs
/// RngState, whose namespace the CoreLoopHarness Life↔Baseball boundary scan
/// forbids inside Assets/Simulation/Life — Narrative is the orchestration
/// layer the scan exempts, exactly like EventDispatcher's own RngState note,
/// and the graph writes ride the same path the event applier already uses.
///
/// Conservation and isolation (§8.2):
/// - the avatar's edges are NEVER touched — the avatar is excluded from
///   every candidate structure at build time, and the edge tier filters any
///   hydrated edge touching them;
/// - Child edges are lineage state — never nudged, never re-typed;
/// - existing kinds are stable except the two sanctioned transitions:
///   Friend → Partner (a deep friendship turning romantic, exclusivity
///   honored on both endpoints) and Partner → Friend/Rival (an NPC breakup,
///   the EndPartnership reclassify-never-delete discipline — this is what
///   organically mints the exes §8 wants);
/// - writes go through <see cref="RelationshipGraph"/> (publish-only), so
///   persistence rides the shipped CollectDirty day flush and a Rival edge
///   pushed negative rides the untouched Phase-6 rivalry transport — the
///   one pre-HS-6 edge-driven sim effect, per §8.2, at the same modest
///   magnitudes the event path already writes.
/// </summary>
public sealed class NpcAutonomyService
{
    /// <summary>
    /// §8.1/§11 knobs — first-pass tunable data, the profile-class precedent.
    /// </summary>
    public static class NpcAutonomyProfile
    {
        /// <summary>Weekly, deliberately the family-tick/CoL beat (day % 7 == 0).</summary>
        public const int CadenceDays = 7;

        /// <summary>The §8.1 hard budget: pair interactions per tick, population-independent.</summary>
        public const int MaxPairInteractionsPerWeek = 256;

        /// <summary>Priority tier 1: interactions drawn from same-team pairs (clubhouses breed history).</summary>
        public const int SameTeamShare = 128;

        /// <summary>Priority tier 2: interactions drawn from existing edges (deepen or decay).</summary>
        public const int ExistingEdgeShare = 64;

        // Tier 3 (a bounded random sample of everyone else) takes the
        // remainder — plus any share a tier without candidates forfeits.

        /// <summary>Chance an edgeless interaction mints a new Friend/Rival edge.</summary>
        public const double MintProbability = 0.15;

        /// <summary>Chance a qualifying friendship turns romantic this interaction.</summary>
        public const double PartnerPromoteProbability = 0.04;

        /// <summary>
        /// Minimum post-nudge affinity before a friendship can promote to
        /// Partner. Deliberately modest: under the 256-interaction budget a
        /// given pair is drawn ~20–25 times a SEASON, so organic affinities
        /// live in the 0–30 band — a threshold up at e.g. 60 is provably
        /// unreachable and would make the promotion path dead code (the
        /// harness's organic-romance check is what pins this reachable).
        /// </summary>
        public const int PartnerPromoteMinAffinity = 15;

        /// <summary>Chance an NPC partnership ends this interaction (~weekly churn, real-life-high like the content).</summary>
        public const double BreakupProbability = 0.02;

        /// <summary>Of NPC breakups, the share that end bitter (Rival) rather than amicable (Friend).</summary>
        public const double BreakupBitterShare = 0.4;

        /// <summary>Amicable exes part as friends — the hs_breakup mutual_split constant.</summary>
        public const int BreakupFriendAffinity = 15;

        /// <summary>Bitter exes part as rivals — the hs_breakup break_it_off constant (intensity 25 through the transport).</summary>
        public const int BreakupRivalAffinity = -25;

        /// <summary>Max affinity nudge magnitude per interaction (§8.1's ±1…±3).</summary>
        public const int MaxNudge = 3;

        /// <summary>Floor of the positive-outcome probability at zero compatibility.</summary>
        public const double PositiveFloor = 0.25;

        /// <summary>Range added to the floor as compatibility rises to 1 (perfect proximity ⇒ 0.75 positive).</summary>
        public const double PositiveRange = 0.5;
    }

    private readonly RelationshipGraph _graph;
    private RngState _rng;

    // Weekly candidate structures — rebuilt per tick from the caller's
    // projection, pooled across ticks. All ordering is made deterministic by
    // ordinal sort, never dictionary enumeration order.
    private readonly List<SocialSeed> _population = new(900);
    private readonly Dictionary<string, SocialSeed> _byId = new(900, StringComparer.Ordinal);
    private readonly List<List<int>> _teams = new(64);
    private readonly List<RelationshipSeed> _edges = new(600);
    private readonly List<RelationshipEdge> _partnerScratch = new(8);

    private static readonly Comparison<SocialSeed> ByPlayerId =
        (a, b) => string.CompareOrdinal(a.PlayerId, b.PlayerId);

    private static readonly Comparison<RelationshipSeed> ByCanonicalKey = (a, b) =>
    {
        int first = string.CompareOrdinal(a.PlayerAId, b.PlayerAId);
        return first != 0 ? first : string.CompareOrdinal(a.PlayerBId, b.PlayerBId);
    };

    /// <summary>Interactions actually performed by the most recent tick — observability for the boot log and the harness.</summary>
    public int LastTickInteractions { get; private set; }

    /// <summary>Edges the most recent tick mutated or created (nudges, mints, promotions, breakups).</summary>
    public int LastTickMutations { get; private set; }

    public NpcAutonomyService(RelationshipGraph graph, RngState forkedRng)
    {
        _graph = graph;
        _rng = forkedRng;
    }

    /// <summary>
    /// The weekly cadence gate, exposed so the caller can skip the population
    /// projection entirely on off days — the projection is the caller's cost,
    /// so unlike FamilyService the gate must be checkable before the work.
    /// <see cref="ProcessDay"/> re-checks it regardless (the harness drives
    /// the identical path either way).
    /// </summary>
    public static bool IsTickDay(long day) => day % NpcAutonomyProfile.CadenceDays == 0;

    /// <summary>
    /// Runs the §8 tick when <paramref name="day"/> is a tick day. The
    /// avatar (<paramref name="avatarId"/>, null when no career exists) is
    /// excluded from every candidate pool — player agency stays event/UI
    /// driven. <paramref name="population"/> is the caller's fresh
    /// projection; order does not matter (sorted internally).
    /// </summary>
    public void ProcessDay(long day, string? avatarId, IReadOnlyList<SocialSeed> population)
    {
        if (!IsTickDay(day))
        {
            return;
        }
        RunWeek(avatarId, population);
    }

    /// <summary>The tick body, cadence-free — the harness's deterministic entry point.</summary>
    public void RunWeek(string? avatarId, IReadOnlyList<SocialSeed> population)
    {
        LastTickInteractions = 0;
        LastTickMutations = 0;
        BuildCandidates(avatarId, population);
        if (_population.Count < 2)
        {
            return;
        }

        // Tier shares: a tier with no candidates forfeits its share to the
        // random tier rather than burning budget on guaranteed skips.
        int sameTeamBudget = _teams.Count > 0 ? NpcAutonomyProfile.SameTeamShare : 0;
        int edgeBudget = _edges.Count > 0 ? NpcAutonomyProfile.ExistingEdgeShare : 0;
        int randomBudget = NpcAutonomyProfile.MaxPairInteractionsPerWeek - sameTeamBudget - edgeBudget;

        // PINNED draw order (the harness's determinism contract): tier 1's
        // draws are team, memberA, memberB(shifted); tier 2's are edge index;
        // tier 3's are indexA, indexB(shifted); then Interact's own rolls.
        for (int i = 0; i < sameTeamBudget; i++)
        {
            List<int> members = _teams[_rng.NextInt(_teams.Count)];
            int a = _rng.NextInt(members.Count);
            int b = _rng.NextInt(members.Count - 1);
            if (b >= a)
            {
                b++;
            }
            Interact(_population[members[a]], _population[members[b]]);
        }
        for (int i = 0; i < edgeBudget; i++)
        {
            RelationshipSeed edge = _edges[_rng.NextInt(_edges.Count)];
            // Endpoints missing a projection (a person with an edge but no
            // Players row this week) interact with neutral 50/50 stats.
            SocialSeed a = _byId.TryGetValue(edge.PlayerAId, out SocialSeed seedA)
                ? seedA
                : new SocialSeed(edge.PlayerAId, null, 50, 50);
            SocialSeed b = _byId.TryGetValue(edge.PlayerBId, out SocialSeed seedB)
                ? seedB
                : new SocialSeed(edge.PlayerBId, null, 50, 50);
            Interact(a, b);
        }
        for (int i = 0; i < randomBudget; i++)
        {
            int a = _rng.NextInt(_population.Count);
            int b = _rng.NextInt(_population.Count - 1);
            if (b >= a)
            {
                b++;
            }
            Interact(_population[a], _population[b]);
        }
    }

    private void BuildCandidates(string? avatarId, IReadOnlyList<SocialSeed> population)
    {
        _population.Clear();
        _byId.Clear();
        for (int i = 0; i < population.Count; i++)
        {
            SocialSeed seed = population[i];
            // §8.2: the avatar is out of every pool at build time.
            if (avatarId is not null && string.Equals(seed.PlayerId, avatarId, StringComparison.Ordinal))
            {
                continue;
            }
            _population.Add(seed);
            _byId[seed.PlayerId] = seed;
        }
        _population.Sort(ByPlayerId);

        // Team groups (tier 1): grouped in first-seen order over the SORTED
        // population, so the grouping is deterministic regardless of caller
        // order. Member lists are pooled and reused across ticks; the small
        // per-tick index dictionary is weekly-cadence cost, not a hot loop.
        int teamsUsed = 0;
        var teamIndexById = new Dictionary<int, int>(64);
        for (int i = 0; i < _population.Count; i++)
        {
            int? teamId = _population[i].TeamId;
            if (teamId is null)
            {
                continue;
            }
            if (!teamIndexById.TryGetValue(teamId.Value, out int slot))
            {
                if (teamsUsed == _teams.Count)
                {
                    _teams.Add(new List<int>(20));
                }
                slot = teamsUsed++;
                _teams[slot].Clear();
                teamIndexById.Add(teamId.Value, slot);
            }
            _teams[slot].Add(i);
        }
        // Drop sub-2-member groups (no pair to draw) and truncate the pool to
        // this week's count, keeping deterministic ascending-team-id order.
        int kept = 0;
        for (int slot = 0; slot < teamsUsed; slot++)
        {
            if (_teams[slot].Count >= 2)
            {
                (_teams[kept], _teams[slot]) = (_teams[slot], _teams[kept]);
                kept++;
            }
        }
        _teams.RemoveRange(kept, _teams.Count - kept);

        // Edge snapshot (tier 2): Child edges and anything touching the
        // avatar are filtered OUT here, so no edge-tier draw can ever reach
        // them; canonical-key sort de-randomizes dictionary order.
        _graph.CollectEdges(_edges);
        int edgeKept = 0;
        for (int i = 0; i < _edges.Count; i++)
        {
            RelationshipSeed edge = _edges[i];
            if (edge.Kind == RelationshipKind.Child)
            {
                continue;
            }
            if (avatarId is not null
                && (string.Equals(edge.PlayerAId, avatarId, StringComparison.Ordinal)
                    || string.Equals(edge.PlayerBId, avatarId, StringComparison.Ordinal)))
            {
                continue;
            }
            _edges[edgeKept++] = edge;
        }
        _edges.RemoveRange(edgeKept, _edges.Count - edgeKept);
        _edges.Sort(ByCanonicalKey);
    }

    /// <summary>
    /// One pair interaction. Draw order within is pinned: (Partner only) the
    /// breakup roll; then the sign roll + magnitude roll of the nudge/mint;
    /// then (Friend at depth only) the promotion roll.
    /// </summary>
    private void Interact(in SocialSeed a, in SocialSeed b)
    {
        if (string.Equals(a.PlayerId, b.PlayerId, StringComparison.Ordinal))
        {
            return; // defensive; pair draws are distinct by construction
        }
        LastTickInteractions++;

        bool hasEdge = _graph.TryGetRelationship(a.PlayerId, b.PlayerId, out int affinity, out RelationshipKind kind);
        if (hasEdge && kind == RelationshipKind.Child)
        {
            return; // lineage state — never nudged (a same-team parent/child pair can land here)
        }

        if (hasEdge && kind == RelationshipKind.Partner)
        {
            if (_rng.NextDouble() < NpcAutonomyProfile.BreakupProbability)
            {
                bool bitter = _rng.NextDouble() < NpcAutonomyProfile.BreakupBitterShare;
                _graph.SetRelationship(
                    a.PlayerId, b.PlayerId,
                    bitter ? NpcAutonomyProfile.BreakupRivalAffinity : NpcAutonomyProfile.BreakupFriendAffinity,
                    bitter ? RelationshipKind.Rival : RelationshipKind.Friend);
                LastTickMutations++;
                return;
            }
        }

        if (!hasEdge)
        {
            if (_rng.NextDouble() >= NpcAutonomyProfile.MintProbability)
            {
                return; // strangers stay strangers this week
            }
            int mintDelta = RollDelta(in a, in b);
            _graph.SetRelationship(
                a.PlayerId, b.PlayerId, mintDelta,
                mintDelta > 0 ? RelationshipKind.Friend : RelationshipKind.Rival);
            LastTickMutations++;
            return;
        }

        int delta = RollDelta(in a, in b);
        _graph.AdjustAffinity(a.PlayerId, b.PlayerId, delta);
        LastTickMutations++;

        if (kind == RelationshipKind.Friend
            && _graph.TryGetRelationship(a.PlayerId, b.PlayerId, out int newAffinity, out _)
            && newAffinity >= NpcAutonomyProfile.PartnerPromoteMinAffinity
            && _rng.NextDouble() < NpcAutonomyProfile.PartnerPromoteProbability
            && !HasPartner(a.PlayerId) && !HasPartner(b.PlayerId))
        {
            _graph.SetRelationship(a.PlayerId, b.PlayerId, newAffinity, RelationshipKind.Partner);
            LastTickMutations++;
        }
    }

    /// <summary>
    /// §8.1: the ±1…±3 nudge from person-stat compatibility. Proximity on
    /// charisma and teamwork sets the POSITIVE-outcome probability (identical
    /// stats ⇒ 0.75, maximal distance ⇒ 0.25); magnitude is uniform 1–3
    /// either way. Never returns 0, so a mint always has a sign.
    /// </summary>
    private int RollDelta(in SocialSeed a, in SocialSeed b)
    {
        double compat = 1.0
            - (Math.Abs(a.Charisma - b.Charisma) + Math.Abs(a.Teamwork - b.Teamwork)) / 200.0;
        double pPositive = NpcAutonomyProfile.PositiveFloor + NpcAutonomyProfile.PositiveRange * compat;
        bool positive = _rng.NextDouble() < pPositive;
        int magnitude = 1 + _rng.NextInt(NpcAutonomyProfile.MaxNudge);
        return positive ? magnitude : -magnitude;
    }

    /// <summary>Single-partner exclusivity (§3): the promotion guard, checked on BOTH endpoints.</summary>
    private bool HasPartner(string playerId)
    {
        _graph.GetEdgesFor(playerId, _partnerScratch);
        for (int i = 0; i < _partnerScratch.Count; i++)
        {
            if (_partnerScratch[i].Kind == RelationshipKind.Partner)
            {
                return true;
            }
        }
        return false;
    }
}
