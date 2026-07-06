using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// The tier → <see cref="LeagueSimulator"/> map (Phase 9a): one macro-sim
/// instance per ladder tier, each bulk-loading only its own 8-team league.
/// <see cref="CareerManager"/> resolves the avatar's sim through this instead
/// of holding a single simulator, so the attended-team handoff lands on the
/// correct tier's sim (and 9c's promotion handoff has a seam to move across).
///
/// Registration may be sparse: harness fixtures that only exercise one tier
/// register just that sim, and <see cref="Get"/> fails loudly if anything
/// reaches for an unregistered tier.
/// </summary>
public sealed class LeagueDirectory
{
    public const int TierCount = 6;

    private readonly LeagueSimulator?[] _byTier = new LeagueSimulator?[TierCount];

    /// <summary>Registers a simulator under its own <see cref="LeagueSimulator.Tier"/>.</summary>
    public void Register(LeagueSimulator league)
    {
        _byTier[(int)league.Tier] = league;
    }

    public LeagueSimulator Get(LeagueTier tier) =>
        _byTier[(int)tier] ?? throw new InvalidOperationException(
            $"No LeagueSimulator is registered for tier {tier} — was the directory wired for this world?");

    /// <summary>
    /// Sparse-registration-safe lookup (Phase 9c): the promotion pass touches
    /// every tier's roster in the database, but flushes/re-initializes only
    /// the sims a world actually registered — harness fixtures exercising a
    /// subset of the ladder skip the rest instead of throwing.
    /// </summary>
    public bool TryGet(LeagueTier tier, out LeagueSimulator league)
    {
        LeagueSimulator? registered = _byTier[(int)tier];
        league = registered!;
        return registered is not null;
    }
}
