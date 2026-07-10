using System;
using System.Collections.Generic;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Economy.Family;

/// <summary>
/// The weekly child-rearing tick (docs/design/high_school_person_layer.md
/// §7.1) for the avatar's OWN children — the reverse direction from
/// <see cref="FamilyService"/> (which pays the avatar allowance from THEIR
/// parents and stops at graduation). This one keeps running into adulthood:
/// there's no high-school gate, because raising a kid doesn't end when the
/// avatar's diploma does.
///
/// Applies the player's standing weekly funding commitment
/// (Child_Rearing_Commitment) plus the DaySchedule Family block's weekly
/// hour total (threaded in by GameManager from LifeSimManager's
/// PeekFamilyHoursThisWeek — this service never touches Life directly) to
/// every one of the avatar's children uniformly, exactly like
/// EventConsequenceApplier.ApplyChildDevelopment's own "no per-child
/// targeting" precedent. A flat, mandatory per-child child-support expense
/// rides the same cadence, independent of the discretionary commitment —
/// the "basic cost of having a kid" the design doc calls out separately
/// from the player's own allocation choice.
///
/// One batch per tick (the database rules' calendar-tick discipline), funds
/// impulse published only after the commit — FamilyService's exact pattern.
/// </summary>
public sealed class ChildRearingService
{
    /// <summary>Weekly, deliberately the same cadence/day as LifeSimManager.CostOfLivingCadenceDays and FamilyService.FamilyTickCadenceDays.</summary>
    public const int TickCadenceDays = 7;

    // First-pass, disclosed-tunable constants (WeeklyCostOfLiving's posture —
    // no Monte Carlo proof required, this isn't the baseball engine).
    public const double ChildSupportPerChild = 20.0;

    /// <summary>
    /// Weekly family-hours treated as the neutral baseline for the care axis
    /// (below it, care decays; above it, care grows). Calibrated to the
    /// FamilyRow slider (0–24h/day, accumulated across 7 days), not an
    /// arbitrary round number: the old baseline of 2h/WEEK meant any single
    /// ~1h/day habit already saturated the delta, making the axis a near
    /// on/off switch instead of a gradient.
    /// </summary>
    public const float CareNeutralHoursPerWeek = 3f;

    /// <summary>Weekly family hours needed per additional ±1 care-axis point beyond the baseline.</summary>
    public const float CareHoursPerPoint = 2f;

    /// <summary>
    /// Weekly discretionary $ (Child_Rearing_Commitment.weekly_funding,
    /// schema-capped at 300) needed per +1 funding-axis point. Chosen so the
    /// schema's actual $300/wk ceiling lands exactly at the +5 cap — the old
    /// $25 divisor saturated at $150/wk, leaving the top half of the
    /// commitment range with no further effect.
    /// </summary>
    public const int FundingDollarsPerPoint = 50;

    /// <summary>Weekly neglect-axis gain when the avatar logs zero family hours AND zero funding.</summary>
    public const int NeglectAccrualDelta = 3;

    /// <summary>Weekly neglect-axis decay whenever either hours or funding is nonzero.</summary>
    public const int NeglectRecoveryDelta = -1;

    // PersonQueries.AdjustChildAxis's axisIndex contract: the Narrative
    // ChildAxis ordinal, mirrored (not referenced — this assembly stays
    // Narrative-free) per ChildAxisColumns' own contract comment.
    private const int CareAxis = 0;
    private const int FundingAxis = 2;
    private const int NeglectAxis = 3;

    private readonly DatabaseManager _db;
    private readonly PlayerQueries _players;
    private readonly PersonQueries _persons;
    private readonly EventBus _bus;

    // Reused across ticks — a handful of children at most, never a hot loop.
    private readonly List<PlayerRow> _childrenScratch = new(4);

    public ChildRearingService(DatabaseManager db, PlayerQueries players, PersonQueries persons, EventBus bus)
    {
        _db = db;
        _players = players;
        _persons = persons;
        _bus = bus;
    }

    /// <summary>
    /// Runs the child-rearing tick for <paramref name="avatarId"/> when
    /// <paramref name="day"/> is a tick day. Safe to call every day — the
    /// cadence gate lives here so the harness drives it exactly like the
    /// GameManager subscriber does. An avatar with no children is a clean
    /// no-op (the ApplyChildDevelopment "empty-pool" precedent).
    /// </summary>
    public void ProcessDay(string avatarId, long day, float familyHoursThisWeek)
    {
        if (day % TickCadenceDays != 0)
        {
            return;
        }
        _players.LoadChildrenOf(avatarId, _childrenScratch);
        if (_childrenScratch.Count == 0)
        {
            return;
        }

        int weeklyFunding = _persons.TryGetChildRearingCommitment(avatarId, out ChildRearingCommitmentRow commitment)
            ? commitment.WeeklyFunding
            : 0;

        int careDelta = Math.Clamp(
            RoundAwayFromZero((familyHoursThisWeek - CareNeutralHoursPerWeek) / CareHoursPerPoint),
            -2, 5);
        int fundingDelta = Math.Clamp(
            RoundAwayFromZero((double)weeklyFunding / FundingDollarsPerPoint - 1.0),
            -1, 5);
        int neglectDelta = familyHoursThisWeek <= 0f && weeklyFunding <= 0
            ? NeglectAccrualDelta
            : NeglectRecoveryDelta;

        double totalExpense = weeklyFunding + (ChildSupportPerChild * _childrenScratch.Count);

        _db.BeginBatch();
        try
        {
            for (int i = 0; i < _childrenScratch.Count; i++)
            {
                string childId = _childrenScratch[i].PlayerId;
                _persons.AdjustChildAxis(childId, CareAxis, careDelta, day);
                _persons.AdjustChildAxis(childId, FundingAxis, fundingDelta, day);
                _persons.AdjustChildAxis(childId, NeglectAxis, neglectDelta, day);
            }
            if (totalExpense > 0.0)
            {
                _players.AdjustFunds(avatarId, -totalExpense);
            }
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }

        if (totalExpense > 0.0)
        {
            // Post-commit impulse so the Life sim's in-memory funds mirror
            // catches up — FamilyService's exact ordering.
            _bus.Publish(new FundsImpulseEvent(avatarId, -totalExpense));
        }
    }

    // The codebase-wide rounding discipline (NurtureBlend/DevelopmentManager's
    // own private helper of the same name) — away-from-zero, not the CLR's
    // to-even default, so a .5 always breaks toward the larger-magnitude delta.
    private static int RoundAwayFromZero(double value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
