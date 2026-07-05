using System;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Simulation.Hustles;

/// <summary>
/// A single playing card packed into one byte (docs/design/hustles_texas_holdem.md
/// §4.1): <c>Code = (Rank&lt;&lt;2)|Suit</c>, Rank ∈ [2,14] (11=J,12=Q,13=K,14=A),
/// Suit ∈ [0,3]. Compact and alloc-free — decks and hand buffers are
/// <c>Span&lt;Card&gt;</c>/<c>stackalloc</c> throughout the Hold'em resolvers.
/// </summary>
public readonly struct Card : IEquatable<Card>
{
    public readonly byte Code;

    public Card(byte code)
    {
        Code = code;
    }

    public Card(int rank, int suit)
    {
        Code = (byte)((rank << 2) | suit);
    }

    public int Rank => Code >> 2;
    public int Suit => Code & 0b11;

    public bool Equals(Card other) => Code == other.Code;
    public override bool Equals(object? obj) => obj is Card c && Equals(c);
    public override int GetHashCode() => Code;
    public static bool operator ==(Card a, Card b) => a.Code == b.Code;
    public static bool operator !=(Card a, Card b) => a.Code != b.Code;

    private static readonly char[] RankChars = { '2', '3', '4', '5', '6', '7', '8', '9', 'T', 'J', 'Q', 'K', 'A' };
    private static readonly char[] SuitChars = { 'c', 'd', 'h', 's' };

    /// <summary>Debug/harness readability only (e.g. "As", "Td") — never used by the pure resolvers.</summary>
    public override string ToString() => $"{RankChars[Rank - 2]}{SuitChars[Suit]}";
}

/// <summary>Deck construction/shuffling shared by every Hold'em resolver — no heap, <c>Span&lt;Card&gt;</c> in, <c>Span&lt;Card&gt;</c> mutated in place.</summary>
public static class Deck
{
    public const int Size = 52;

    /// <summary><paramref name="deck52"/> must have Length == 52.</summary>
    public static void FillStandard(Span<Card> deck52)
    {
        if (deck52.Length != Size)
        {
            throw new ArgumentException($"Deck buffer must be length {Size}.", nameof(deck52));
        }

        int i = 0;
        for (int rank = 2; rank <= 14; rank++)
        {
            for (int suit = 0; suit < 4; suit++)
            {
                deck52[i++] = new Card(rank, suit);
            }
        }
    }

    /// <summary>In-place Fisher–Yates over <paramref name="rng"/> — the one shuffle every session/hand draws from (§10's single-RNG determinism contract).</summary>
    public static void Shuffle(Span<Card> deck, ref RngState rng)
    {
        for (int i = deck.Length - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
    }
}
