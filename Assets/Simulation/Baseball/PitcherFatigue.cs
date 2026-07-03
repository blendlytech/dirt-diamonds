namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// §8 pitcher stamina &amp; fatigue — the micro-sim mechanic that finally binds
/// Player_Ratings.pit_stamina and the PED 1.5× stamina hook. A pitcher starts
/// with a pitch capacity derived from (PED-adjusted) stamina, stays fresh
/// through a comfort fraction of it, then declines non-linearly; the resulting
/// multiplier m ∈ [MinMultiplier, 1] decays *effective* stuff/control toward
/// league-average (50), which are then fed to the same shared
/// <see cref="AtBatResolver"/> — no bespoke fatigue penalty on outcomes.
///
/// Mutable struct carried by the game driver; allocation-free.
/// </summary>
public struct PitcherFatigue
{
    // §8.1 capacity knobs: rating 0 → 70 pitches, 50 → 95, 100 → 120.
    public const double StaminaBase = 70.0;
    public const double StaminaSlope = 0.5;

    // §8.2 curve knobs: flat through ComfortFraction of capacity, then a
    // quadratic (accelerating) decline reaching m(1) = 1 − FatigueC, floored
    // at MinMultiplier for the truly gassed arm.
    public const double ComfortFraction = 0.6;
    public const double FatigueC = 0.25;
    public const double MinMultiplier = 0.5;

    /// <summary>§8.4 PED stamina hook — symmetric with the batter power hook in AtBatResolver.</summary>
    public const double PedStaminaMultiplier = 1.5;

    /// <summary>
    /// Average pitch cost charged for an NPC PA the macro resolver settles in
    /// one draw (no pitch chain) — keeps the starter's clock running through
    /// the whole attended game. Matches the §11 pitches/PA realism band.
    /// </summary>
    public const double PitchesPerNpcPa = 3.9;

    private readonly PitcherRatings _ratings;
    private readonly bool _pedActive;
    private readonly bool _enabled;
    private readonly double _capacity;
    private double _pitchesThrown;

    /// <param name="enabled">False freezes m ≡ 1 (harness §11 test 2 isolates
    /// neutral consistency from fatigue drift).</param>
    public PitcherFatigue(in PitcherRatings ratings, bool pedActive, bool enabled = true)
    {
        _ratings = ratings;
        _pedActive = pedActive;
        _enabled = enabled;
        // Integer round-half-up 1.5×, clamped — same form as the resolver's
        // power clamp so the two PED hooks can never disagree on rounding.
        int effectiveStamina = pedActive
            ? Math.Min(100, (ratings.Stamina * 3 + 1) / 2)
            : ratings.Stamina;
        _capacity = StaminaBase + StaminaSlope * effectiveStamina;
        _pitchesThrown = 0.0;
    }

    public readonly double Capacity => _capacity;
    public readonly double PitchesThrown => _pitchesThrown;
    public readonly bool PedActive => _pedActive;
    public readonly PitcherRatings RestedRatings => _ratings;

    /// <summary>§8.2 fatigue multiplier m(x), x = pitches / capacity.</summary>
    public readonly double Multiplier
    {
        get
        {
            if (!_enabled)
            {
                return 1.0;
            }
            double x = _pitchesThrown / _capacity;
            if (x <= ComfortFraction)
            {
                return 1.0;
            }
            double t = (x - ComfortFraction) / (1.0 - ComfortFraction);
            return Math.Max(MinMultiplier, 1.0 - FatigueC * t * t);
        }
    }

    public void AddPitch() => _pitchesThrown += 1.0;

    public void AddNpcPa() => _pitchesThrown += PitchesPerNpcPa;

    /// <summary>
    /// §8.3 effective ratings: stuff/control decay toward 50 by the current
    /// multiplier, rounded back to the resolver's integer rating domain.
    /// Stamina passes through untouched (it is not a §4 matchup input).
    /// </summary>
    public readonly PitcherRatings EffectiveRatings()
    {
        double m = Multiplier;
        if (m >= 1.0)
        {
            return _ratings;
        }
        byte stuff = (byte)Math.Round(50.0 + (_ratings.Stuff - 50) * m, MidpointRounding.AwayFromZero);
        byte control = (byte)Math.Round(50.0 + (_ratings.Control - 50) * m, MidpointRounding.AwayFromZero);
        return new PitcherRatings(stuff, control, _ratings.Stamina);
    }
}
