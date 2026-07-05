using System;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Simulation.Hustles;

/// <summary>
/// Pot-odds/MDF/bluff decision math (docs/design/hustles_texas_holdem.md §6)
/// and the archetype-driven decision procedure (§7) shared by every opponent
/// seat and, at TAG tunables reck-interpolated toward Maniac, the hero
/// autopilot (§7.3). Pure — reads the live <see cref="HoldemHandState"/> but
/// never mutates it, aside from advancing <c>ref RngState</c> for the equity
/// MC and the bluff/random rolls.
/// </summary>
public static class HoldemAgent
{
    /// <summary>
    /// §6.1's calling threshold, <c>c / P</c>, where <paramref name="pot"/> is
    /// the running total pot at decision time (it already includes the bet
    /// being faced — <c>CommitChips</c> adds a bet to <see cref="HoldemHandState.Pot"/>
    /// the instant it happens). Algebraically this is exactly <c>s/(1+s)</c>
    /// for <c>s = bet / potBeforeThatBet</c> (pot-sized ⇒ 50%, half-pot ⇒ 33%
    /// — matching §6.1's own worked examples) — the SAME expression §6.2
    /// derives for MDF, per §6.2's closing claim ("MDF and pot odds are two
    /// views of the same line"). This equivalence is load-bearing, not
    /// cosmetic: §6.3's bluff-ratio is the value:bluff mix that makes a
    /// caller defending at exactly this threshold indifferent between calling
    /// and folding. The more common "textbook" pot-odds phrasing — c/(pot+c),
    /// treating <paramref name="pot"/> as excluding the call — was tried
    /// first and is mathematically the DIFFERENT quantity s/(1+2s); using it
    /// here made every archetype's calls "too cheap" relative to what the
    /// bluff-ratio assumed, so bluffing was never breakeven and every
    /// archetype's own aggression became a pure leak — measured directly via
    /// <c>--debug-archetypes</c>, where win-rate ranked by bluff frequency
    /// (least-bluffy always won) instead of by decision quality. Restoring
    /// the doc's own <c>c/P</c> fixed it: TAG &gt; LAG ≈ Nit &gt; Maniac ≈
    /// Station against a homogeneous field, as §13 specifies.
    /// </summary>
    public static double RequiredEquity(long owed, long pot) => pot <= 0 ? 0 : owed / (double)pot;

    /// <summary>§6.2: minimum defense frequency for a bet sized <paramref name="sizeFracOfPot"/>×pot — defend at least this share, fold at most <c>1-MDF</c>.</summary>
    public static double MinimumDefenseFrequency(double sizeFracOfPot) => 1.0 / (1.0 + sizeFracOfPot);

    /// <summary>§6.3: the GTO-neutral fraction of a betting range that should be bluffs for a bet sized <paramref name="sizeFracOfPot"/>×pot (pot-size ⇒ 1/3, half-pot ⇒ 1/4 — matches the doc's own worked ratios exactly).</summary>
    public static double BluffFraction(double sizeFracOfPot) => sizeFracOfPot / (1.0 + 2.0 * sizeFracOfPot);

    /// <summary>§7.3: the neutral hero autopilot — TAG tunables, linearly interpolated toward Maniac's ValueMargin/BluffMult by <paramref name="reck"/> ∈ [0,1] (reck=0 ⇒ exactly TAG, reck=1 ⇒ exactly Maniac's aggression — never past it).</summary>
    public static HoldemProfile.ArchetypeTunables HeroAutopilotProfile(double reck)
    {
        reck = Math.Clamp(reck, 0, 1);
        HoldemProfile.ArchetypeTunables tag = HoldemProfile.GetArchetype(HoldemArchetype.TAG);
        HoldemProfile.ArchetypeTunables maniac = HoldemProfile.GetArchetype(HoldemArchetype.Maniac);
        return new HoldemProfile.ArchetypeTunables(
            tag.PreflopEntryEquity,
            tag.ValueMargin + reck * (maniac.ValueMargin - tag.ValueMargin),
            tag.BluffMult + reck * (maniac.BluffMult - tag.BluffMult),
            tag.BetSizeFrac);
    }

    /// <summary>
    /// §7.2 "one agent, one action" — a preflop entry-equity gate (which
    /// hands the archetype voluntarily plays) layered on top of the shared
    /// pot-odds/value/bluff procedure. Never mutates <paramref name="hand"/>.
    /// </summary>
    public static HeroAction DecideAction(HoldemHandState hand, int seat, in HoldemProfile.ArchetypeTunables profile, ref RngState rng)
    {
        int liveOpponents = hand.CountLiveNonFolded() - 1;
        long owe = hand.CurrentBet - hand.CommittedStreet[seat];

        if (hand.CurrentStreet == Street.Preflop && PreflopEntryEquity(hand, seat) < profile.PreflopEntryEquity)
        {
            return owe <= 0 ? HeroAction.CheckOrCall() : HeroAction.Fold();
        }

        double equity = Equity(hand, seat, liveOpponents, ref rng);

        if (owe > 0)
        {
            double potOdds = RequiredEquity(owe, hand.Pot);
            if (equity >= potOdds + profile.ValueMargin)
            {
                return RaiseAction(hand, seat, in profile, isRaise: true);
            }
            if (equity >= potOdds)
            {
                return HeroAction.CheckOrCall();
            }
            double bluffProb = Math.Clamp(BluffFraction(profile.BetSizeFrac) * profile.BluffMult, 0, 1);
            if (rng.NextDouble() < bluffProb)
            {
                return RaiseAction(hand, seat, in profile, isRaise: true);
            }
            return HeroAction.Fold();
        }
        else
        {
            if (equity >= 0.5 + profile.ValueMargin)
            {
                return RaiseAction(hand, seat, in profile, isRaise: false);
            }
            double bluffProb = Math.Clamp(BluffFraction(profile.BetSizeFrac) * profile.BluffMult, 0, 1);
            if (rng.NextDouble() < bluffProb)
            {
                return RaiseAction(hand, seat, in profile, isRaise: false);
            }
            return HeroAction.CheckOrCall();
        }
    }

    /// <summary>Convenience overload matching <see cref="PokerPolicy"/>'s shape, looking the tunables up from the named archetype.</summary>
    public static HeroAction DecideAction(HoldemHandState hand, int seat, HoldemArchetype archetype, ref RngState rng) =>
        DecideAction(hand, seat, HoldemProfile.GetArchetype(archetype), ref rng);

    /// <summary>
    /// The §13 "random/coin-flip-quality" harness policy — equity-blind, acts
    /// uniformly among the legal options. Used only to prove the rake
    /// genuinely binds a mediocre player (check 6); never a named archetype.
    /// </summary>
    public static HeroAction RandomPolicyDecision(HoldemHandState hand, int seat, ref RngState rng)
    {
        long owe = hand.CurrentBet - hand.CommittedStreet[seat];
        double roll = rng.NextDouble();
        if (owe > 0)
        {
            if (roll < 1.0 / 3.0)
            {
                return HeroAction.Fold();
            }
            if (roll < 2.0 / 3.0)
            {
                return HeroAction.CheckOrCall();
            }
            return RaiseAction(hand, seat, HoldemProfile.GetArchetype(HoldemArchetype.TAG), isRaise: true);
        }
        return roll < 0.5
            ? HeroAction.CheckOrCall()
            : RaiseAction(hand, seat, HoldemProfile.GetArchetype(HoldemArchetype.TAG), isRaise: false);
    }

    private static HeroAction RaiseAction(HoldemHandState hand, int seat, in HoldemProfile.ArchetypeTunables profile, bool isRaise)
    {
        long owe = Math.Max(0, hand.CurrentBet - hand.CommittedStreet[seat]);
        long available = hand.Stack[seat] + hand.CommittedStreet[seat];
        long desiredTotal = isRaise
            ? hand.CurrentBet + (long)Math.Round(profile.BetSizeFrac * (hand.Pot + owe))
            : hand.CommittedStreet[seat] + (long)Math.Round(profile.BetSizeFrac * Math.Max(hand.Pot, hand.BigBlind));

        long minLegalTotal = hand.CurrentBet + hand.MinRaiseIncrement;
        long total = Math.Min(Math.Max(desiredTotal, minLegalTotal), available);

        if (total <= hand.CurrentBet)
        {
            // Not even enough chips left to make a legal raise/bet — shove if that's
            // still an increase over the current bet, else it degrades to a call.
            return available > hand.CurrentBet ? HeroAction.RaiseTo(available) : HeroAction.CheckOrCall();
        }
        return HeroAction.RaiseTo(total);
    }

    /// <summary>
    /// §7.1's preflop entry threshold is an intrinsic hand-strength ranking
    /// ("is this hand worth playing at all"), not a function of how many
    /// opponents happen to still be live at THIS table — the same way real
    /// preflop charts rank hands independent of table size. Always looks the
    /// hand up against 1 opponent (its baked heads-up equity), regardless of
    /// <see cref="HoldemHandState.CountLiveNonFolded"/>. Using the actual
    /// live-opponent count here instead made even AA (≈49% at a full 6-max
    /// table) fail every archetype's threshold, collapsing VPIP toward 0 —
    /// caught via the check-6 EV bands going wildly non-monotone.
    /// </summary>
    private static double PreflopEntryEquity(HoldemHandState hand, int seat)
    {
        Card c0 = hand.HoleCards[seat * 2];
        Card c1 = hand.HoleCards[seat * 2 + 1];
        int idx = HoldemEquity.StartingHandIndex(c0.Rank, c1.Rank, suited: c0.Suit == c1.Suit);
        return HoldemEquity.LookupPreflopEquity(idx, liveOpponents: 1);
    }

    private static double Equity(HoldemHandState hand, int seat, int liveOpponents, ref RngState rng)
    {
        Span<Card> hole = stackalloc Card[2] { hand.HoleCards[seat * 2], hand.HoleCards[seat * 2 + 1] };
        if (hand.CurrentStreet == Street.Preflop)
        {
            bool suited = hole[0].Suit == hole[1].Suit;
            int idx = HoldemEquity.StartingHandIndex(hole[0].Rank, hole[1].Rank, suited);
            return HoldemEquity.LookupPreflopEquity(idx, liveOpponents);
        }
        ReadOnlySpan<Card> board = hand.Board.AsSpan(0, hand.BoardCount);
        return HoldemEquity.Estimate(hole, board, liveOpponents, HoldemProfile.EquitySamplesDefault, ref rng);
    }
}
