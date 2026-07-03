using System.Numerics;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// Seedable xoshiro256** PRNG as a mutable struct, passed by <c>ref</c> through
/// the hot simulation loops — no <see cref="Random"/> instance, no heap state,
/// bit-for-bit reproducible from a seed (the run_monte_carlo_batch determinism
/// contract, design doc §8/§9).
/// </summary>
public struct RngState
{
    private ulong _s0, _s1, _s2, _s3;

    public RngState(ulong seed)
    {
        // splitmix64 expansion — well-distributed state from any seed, and the
        // all-zero state (which xoshiro cannot leave) is unreachable.
        _s0 = SplitMix64(ref seed);
        _s1 = SplitMix64(ref seed);
        _s2 = SplitMix64(ref seed);
        _s3 = SplitMix64(ref seed);
    }

    private static ulong SplitMix64(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        ulong z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public ulong NextUInt64()
    {
        ulong result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);
        return result;
    }

    /// <summary>Uniform in [0, 1) with full 53-bit mantissa resolution.</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform integer in [0, maxExclusive). World-gen convenience, not a hot-loop API.</summary>
    public int NextInt(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        return (int)(NextDouble() * maxExclusive);
    }
}
