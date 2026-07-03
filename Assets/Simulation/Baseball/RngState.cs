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

    // xoshiro256** long-jump polynomial (Blackman & Vigna reference constants):
    // advances a state copy by 2^192 draws, partitioning the sequence into
    // non-overlapping streams.
    private static readonly ulong[] LongJump =
    {
        0x76E15D3EFEFDCBBFUL, 0xC5004E441C522FB3UL, 0x77710069854EE241UL, 0x39109BB02ACBE635UL,
    };

    /// <summary>
    /// Forks a non-overlapping child stream 2^192 draws ahead WITHOUT advancing
    /// this state — callers that must stay bit-identical (the M1 generation
    /// prefix) hand later-added consumers a split instead of sharing draws.
    /// </summary>
    public readonly RngState Split()
    {
        RngState child = this;
        ulong s0 = 0, s1 = 0, s2 = 0, s3 = 0;
        foreach (ulong mask in LongJump)
        {
            for (int bit = 0; bit < 64; bit++)
            {
                if ((mask & (1UL << bit)) != 0)
                {
                    s0 ^= child._s0;
                    s1 ^= child._s1;
                    s2 ^= child._s2;
                    s3 ^= child._s3;
                }
                child.NextUInt64();
            }
        }
        child._s0 = s0;
        child._s1 = s1;
        child._s2 = s2;
        child._s3 = s3;
        return child;
    }
}
