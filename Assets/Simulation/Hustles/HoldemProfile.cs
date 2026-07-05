namespace DirtAndDiamonds.Simulation.Hustles;

/// <summary>
/// Shared tunable constants for the Hold'em resolvers (docs/design/hustles_texas_holdem.md).
/// Retuning is a data edit here, never a logic edit — the same convention as
/// <see cref="NarcoticsHustle.NarcoticsProfile"/>/<see cref="FencingNegotiation.FencingProfile"/>.
/// 8d-1 populates only the equity-engine knob; betting/rake/archetype/session
/// constants land with 8d-2.
/// </summary>
public static class HoldemProfile
{
    /// <summary>§5.1: MC sample count for in-decision opponent/hero equity estimates — accuracy ±~3% is plenty for a fold/call threshold.</summary>
    public const int EquitySamplesDefault = 300;
}
