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

        int careDelta = Math.Clamp((int)MathF.Round(familyHoursThisWeek) - 2, -3, 5);
        int fundingDelta = Math.Clamp(weeklyFunding / 25 - 1, -3, 5);
        int neglectDelta = familyHoursThisWeek <= 0f && weeklyFunding <= 0 ? 3 : -1;

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
}
