namespace DirtAndDiamonds.Simulation.Hustles;

/// <summary>
/// The bundle of deltas a resolved hustle run (Narcotics) or negotiation
/// (Fencing) applies to the subject player — the same primitive vocabulary
/// gritty events already write (funds/stress/flags), plus the two risk
/// writers 8b introduces (detection_risk/health_ceiling) and the faction
/// affinity deltas Narcotics alone produces (docs/design/hustles_narcotics_fencing.md
/// §5). Fencing's resolution leaves SupplierTrustDelta/CrewStandingDelta/
/// SetBadProductFlag/SetControlsTurfFlag at their defaults (0/false) — a
/// harmless no-op when <see cref="Economy.Hustles.HustleService"/> applies it,
/// so both hustle types share one resolution shape without Fencing ever
/// touching the faction graph. Hold'em (8d) is the third consumer — it
/// leaves the two faction deltas at 0 (no faction graph either) and adds the
/// one new field below (§1's "one additive struct change, nothing else").
/// </summary>
public readonly struct HustleResolution
{
    public readonly double FundsDelta;
    public readonly int DetectionRiskDelta;
    public readonly int HealthCeilingDelta;
    public readonly int RecklessnessDelta;
    public readonly double StressDelta;

    /// <summary>Narcotics only — a plain delta the resolver computes from context; HustleService owns which live edge it targets (§3.3).</summary>
    public readonly int SupplierTrustDelta;

    /// <summary>Narcotics only — see <see cref="SupplierTrustDelta"/>.</summary>
    public readonly int CrewStandingDelta;

    public readonly bool SetWatchlistFlag;
    public readonly bool SetBadProductFlag;
    public readonly bool SetSpoiledGoodsFlag;
    public readonly bool SetControlsTurfFlag;

    /// <summary>Hold'em only (docs/design/hustles_texas_holdem.md §9/§11) — set on a raid, the poker analogue of Fencing's sting flag ("gambling_bust").</summary>
    public readonly bool SetGamblingBustFlag;

    /// <summary>Robbery only (docs/design/hustle_minigames_depth_pass.md §5.2) — set on a botched execute/getaway, the robbery analogue of the sting/raid flags ("robbery_bust"). The R-2 <see cref="Economy.Hustles.HustleService"/> apply path consumes it; leaving it defaulted keeps every other hustle a no-op, exactly like the four flags above.</summary>
    public readonly bool SetRobberyBustFlag;

    public HustleResolution(
        double fundsDelta, int detectionRiskDelta, int healthCeilingDelta, int recklessnessDelta, double stressDelta,
        int supplierTrustDelta, int crewStandingDelta,
        bool setWatchlistFlag, bool setBadProductFlag, bool setSpoiledGoodsFlag, bool setControlsTurfFlag,
        bool setGamblingBustFlag = false, bool setRobberyBustFlag = false)
    {
        FundsDelta = fundsDelta;
        DetectionRiskDelta = detectionRiskDelta;
        HealthCeilingDelta = healthCeilingDelta;
        RecklessnessDelta = recklessnessDelta;
        StressDelta = stressDelta;
        SupplierTrustDelta = supplierTrustDelta;
        CrewStandingDelta = crewStandingDelta;
        SetWatchlistFlag = setWatchlistFlag;
        SetBadProductFlag = setBadProductFlag;
        SetSpoiledGoodsFlag = setSpoiledGoodsFlag;
        SetControlsTurfFlag = setControlsTurfFlag;
        SetGamblingBustFlag = setGamblingBustFlag;
        SetRobberyBustFlag = setRobberyBustFlag;
    }
}
