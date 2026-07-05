using System;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Simulation.Hustles;

public enum Street : byte { Preflop, Flop, Turn, River }

public enum SeatStatus : byte { Active, Folded, AllIn }

/// <summary>
/// One action a seat takes at a decision point (docs/design/hustles_texas_holdem.md
/// §8.2): Fold, CheckOrCall (resolves to a check if nothing is owed, else a
/// call — capping at all-in if the seat is short), or RaiseTo(total) — the
/// TOTAL amount the seat is betting/raising TO this street, not the
/// increment. One shape for both the hero's real input and whatever
/// <see cref="HoldemAgent"/> computes internally for an AI seat.
/// </summary>
public readonly struct HeroAction
{
    public enum ActionKind : byte { Fold, CheckOrCall, RaiseTo }

    public readonly ActionKind Kind;
    public readonly long RaiseToAmount;

    private HeroAction(ActionKind kind, long raiseToAmount)
    {
        Kind = kind;
        RaiseToAmount = raiseToAmount;
    }

    public static HeroAction Fold() => new(ActionKind.Fold, 0);
    public static HeroAction CheckOrCall() => new(ActionKind.CheckOrCall, 0);
    public static HeroAction RaiseTo(long amount) => new(ActionKind.RaiseTo, amount);
}

/// <summary>One recorded action for the UI's "opponents I missed" replay (§8.2) — structured, not pre-formatted text, so the autopilot/harness hot path stays alloc-free; Layer 3 renders this lazily.</summary>
public readonly struct HandActionLogEntry
{
    public readonly int SeatIndex;
    public readonly Street Street;
    public readonly HeroAction.ActionKind Kind;
    public readonly long Amount;

    public HandActionLogEntry(int seatIndex, Street street, HeroAction.ActionKind kind, long amount)
    {
        SeatIndex = seatIndex;
        Street = street;
        Kind = kind;
        Amount = amount;
    }
}

/// <summary>
/// Net result of one completed hand, read off <see cref="HoldemHandState.Result"/>
/// once <see cref="HoldemHandState.HandComplete"/> is true. The two arrays are
/// the hand state's own pooled scratch buffers (only indices [0, SeatCount)
/// are meaningful) — reused and overwritten by the next <see cref="HoldemHandState.StartHand"/>,
/// so a caller must consume a result before starting the next hand.
/// </summary>
public readonly struct HandResult
{
    public readonly long[] NetChipChange;
    public readonly int[] ShowdownScore;
    public readonly long Rake;
    public readonly bool WentToShowdown;

    public HandResult(long[] netChipChange, int[] showdownScore, long rake, bool wentToShowdown)
    {
        NetChipChange = netChipChange;
        ShowdownScore = showdownScore;
        Rake = rake;
        WentToShowdown = wentToShowdown;
    }
}

/// <summary>A seat's decision function, given the live table and its own seat index — used to override the default archetype-driven <see cref="HoldemAgent.DecideAction"/> path (e.g. the §13 equity-blind random policy). Takes <c>ref RngState</c> as a plain parameter (never captured), so ordinary methods satisfy it with no closure/allocation.</summary>
public delegate HeroAction PokerPolicy(HoldemHandState hand, int seat, ref RngState rng);

/// <summary>
/// The no-limit betting state machine for one hand (docs/design/hustles_texas_holdem.md
/// §8): streets, side-pot-correct all-in resolution, and the interactive
/// contract (<see cref="StartHand"/>/<see cref="SubmitHeroAction"/>) that
/// advances to the next hero decision point (or hand end) on each call,
/// batching opponent actions between hero turns into <see cref="ActionLog"/>.
///
/// State-representation call (§10): a <b>pooled mutable class</b>, one
/// instance reused across a whole session — the design doc's sanctioned
/// fallback to the InlineArray-struct recommendation, chosen here because
/// it is simpler to get right for a table this size and is equally
/// zero-alloc once warm (poker is turn-based, not a per-frame hot path).
/// Flagged for Fable's review per the design doc's own "Fable's call" framing.
/// </summary>
public sealed class HoldemHandState
{
    public const int MaxSeats = 6;
    private const int MaxActionLogEntries = 96;

    // Seat identity + chips — persist hand-to-hand within a session (this
    // object IS the session's table; a session never creates a second one).
    public readonly HoldemArchetype[] Archetype = new HoldemArchetype[MaxSeats];
    public readonly bool[] IsHero = new bool[MaxSeats];
    public readonly long[] Stack = new long[MaxSeats];
    public readonly PokerPolicy?[] SeatPolicyOverride = new PokerPolicy?[MaxSeats];
    public int SeatCount;
    public int ButtonSeat;
    public long SmallBlind;
    public long BigBlind;

    // Per-hand transient state, reset by StartHand.
    public readonly Card[] HoleCards = new Card[MaxSeats * 2];
    public readonly SeatStatus[] Status = new SeatStatus[MaxSeats];
    public readonly long[] CommittedTotal = new long[MaxSeats];
    public readonly long[] CommittedStreet = new long[MaxSeats];
    public readonly Card[] Board = new Card[5];
    public int BoardCount;
    public Street CurrentStreet;
    public long Pot;
    public long CurrentBet;
    public long MinRaiseIncrement;
    public int ActingSeat;
    public bool AwaitingHero;
    public bool HandComplete;
    public bool SawFlop;

    public readonly HandActionLogEntry[] ActionLog = new HandActionLogEntry[MaxActionLogEntries];
    public int ActionLogCount;

    public HandResult Result;

    private readonly long[] _netChipChangeScratch = new long[MaxSeats];
    private readonly int[] _showdownScoreScratch = new int[MaxSeats];
    private readonly Card[] _deck = new Card[52];
    private int _deckPos;
    private int _sbSeat;
    private int _bbSeat;
    private int _remainingToAct;

    /// <summary>Seats the table (session start, or a bust-and-replace) — persistent identity only, no per-hand state.</summary>
    public void ConfigureSeats(
        int seatCount, ReadOnlySpan<HoldemArchetype> archetypes, int heroSeat,
        ReadOnlySpan<long> startingStacks, int buttonSeat, long smallBlind, long bigBlind)
    {
        if (seatCount < 2 || seatCount > MaxSeats)
        {
            throw new ArgumentOutOfRangeException(nameof(seatCount), seatCount, $"seatCount must be in [2,{MaxSeats}].");
        }

        SeatCount = seatCount;
        for (int i = 0; i < seatCount; i++)
        {
            Archetype[i] = archetypes[i];
            IsHero[i] = i == heroSeat;
            Stack[i] = startingStacks[i];
            SeatPolicyOverride[i] = null;
        }
        ButtonSeat = buttonSeat % seatCount;
        SmallBlind = smallBlind;
        BigBlind = bigBlind;
    }

    /// <summary>Tops a seat's stack up between hands (the session's rebuy/rebank policy) — never called mid-hand.</summary>
    public void SetStack(int seat, long stack) => Stack[seat] = stack;

    public void RotateButton() => ButtonSeat = (ButtonSeat + 1) % SeatCount;

    public int CountActiveSeats()
    {
        int n = 0;
        for (int i = 0; i < SeatCount; i++)
        {
            if (Status[i] == SeatStatus.Active) n++;
        }
        return n;
    }

    public int CountLiveNonFolded()
    {
        int n = 0;
        for (int i = 0; i < SeatCount; i++)
        {
            if (Status[i] != SeatStatus.Folded) n++;
        }
        return n;
    }

    /// <summary>Deals, posts blinds, and runs opponents up to the hero's first decision (or hand end) — §8.2.</summary>
    public void StartHand(ref RngState rng)
    {
        ResetHand();
        DealHoleCards(ref rng);
        PostBlinds();
        BeginBettingRound(BigBlind, PreflopFirstToAct());
        RunUntilHeroOrComplete(ref rng);
    }

    /// <summary>Applies the hero's action and runs opponents/streets until the hero must act again or the hand ends — §8.2.</summary>
    public void SubmitHeroAction(HeroAction action, ref RngState rng)
    {
        if (!AwaitingHero)
        {
            throw new InvalidOperationException("SubmitHeroAction called when no hero decision is pending.");
        }
        // Validate/apply before clearing AwaitingHero — an illegal action throws
        // without consuming the pending decision, so the caller can retry with a
        // legal one instead of stranding the hand.
        ApplyAction(ActingSeat, action, ref rng);
        AwaitingHero = false;
        RunUntilHeroOrComplete(ref rng);
    }

    // ------------------------------------------------------------------
    // Hand setup
    // ------------------------------------------------------------------

    private void ResetHand()
    {
        for (int i = 0; i < SeatCount; i++)
        {
            Status[i] = SeatStatus.Active;
            CommittedTotal[i] = 0;
            CommittedStreet[i] = 0;
        }
        BoardCount = 0;
        Pot = 0;
        CurrentBet = 0;
        MinRaiseIncrement = BigBlind;
        CurrentStreet = Street.Preflop;
        AwaitingHero = false;
        HandComplete = false;
        SawFlop = false;
        ActionLogCount = 0;
        Result = default;
        _deckPos = 0;
    }

    private void DealHoleCards(ref RngState rng)
    {
        Deck.FillStandard(_deck);
        Deck.Shuffle(_deck, ref rng);
        for (int i = 0; i < SeatCount; i++)
        {
            HoleCards[i * 2] = _deck[_deckPos++];
            HoleCards[i * 2 + 1] = _deck[_deckPos++];
        }
    }

    private void PostBlinds()
    {
        if (SeatCount == 2)
        {
            _sbSeat = ButtonSeat;
            _bbSeat = (ButtonSeat + 1) % 2;
        }
        else
        {
            _sbSeat = (ButtonSeat + 1) % SeatCount;
            _bbSeat = (ButtonSeat + 2) % SeatCount;
        }
        CommitChips(_sbSeat, SmallBlind);
        CommitChips(_bbSeat, BigBlind);
    }

    private int PreflopFirstToAct() => SeatCount == 2 ? _sbSeat : (_bbSeat + 1) % SeatCount;

    private int PostflopFirstToAct() => FindNextActive((ButtonSeat + 1) % SeatCount, inclusive: true);

    // ------------------------------------------------------------------
    // Betting round mechanics
    // ------------------------------------------------------------------

    private void BeginBettingRound(long openingBet, int firstToAct)
    {
        CurrentBet = openingBet;
        MinRaiseIncrement = BigBlind;
        int activeCount = CountActiveSeats();
        if (activeCount <= 1)
        {
            // No more betting is possible (at most one seat still has chips to
            // wager) — the street closes immediately and the board just runs out.
            _remainingToAct = 0;
            return;
        }
        _remainingToAct = activeCount;
        int next = FindNextActive(firstToAct, inclusive: true);
        ActingSeat = next < 0 ? firstToAct : next;
    }

    private int FindNextActive(int from, bool inclusive)
    {
        int start = inclusive ? from : (from + 1) % SeatCount;
        for (int step = 0; step < SeatCount; step++)
        {
            int idx = (start + step) % SeatCount;
            if (Status[idx] == SeatStatus.Active)
            {
                return idx;
            }
        }
        return -1;
    }

    private void CommitChips(int seat, long amount)
    {
        long pay = Math.Min(amount, Stack[seat]);
        Stack[seat] -= pay;
        CommittedStreet[seat] += pay;
        CommittedTotal[seat] += pay;
        Pot += pay;
        if (Stack[seat] == 0 && Status[seat] == SeatStatus.Active)
        {
            Status[seat] = SeatStatus.AllIn;
        }
    }

    private void LogAction(int seat, HeroAction.ActionKind kind, long amount)
    {
        if (ActionLogCount < MaxActionLogEntries)
        {
            ActionLog[ActionLogCount++] = new HandActionLogEntry(seat, CurrentStreet, kind, amount);
        }
    }

    /// <summary>Validates and applies one seat's action (§8.2's "illegal actions fail loud" contract) — shared by the hero path and the internal AI loop.</summary>
    private void ApplyAction(int seat, HeroAction action, ref RngState rng)
    {
        switch (action.Kind)
        {
            case HeroAction.ActionKind.Fold:
                Status[seat] = SeatStatus.Folded;
                LogAction(seat, HeroAction.ActionKind.Fold, 0);
                _remainingToAct--;
                ActingSeat = FindNextActive(seat, inclusive: false);
                break;

            case HeroAction.ActionKind.CheckOrCall:
            {
                long owe = CurrentBet - CommittedStreet[seat];
                long pay = Math.Max(0, Math.Min(owe, Stack[seat]));
                CommitChips(seat, pay);
                LogAction(seat, HeroAction.ActionKind.CheckOrCall, CommittedStreet[seat]);
                _remainingToAct--;
                ActingSeat = FindNextActive(seat, inclusive: false);
                break;
            }

            case HeroAction.ActionKind.RaiseTo:
            {
                long total = action.RaiseToAmount;
                long available = Stack[seat] + CommittedStreet[seat];
                if (total > available)
                {
                    throw new ArgumentOutOfRangeException(nameof(action), total, $"Raise-to {total} exceeds seat {seat}'s available chips ({available}).");
                }
                if (total <= CurrentBet)
                {
                    throw new ArgumentOutOfRangeException(nameof(action), total, $"Raise-to {total} must exceed the current bet ({CurrentBet}) — use CheckOrCall to call.");
                }
                bool isAllIn = total == available;
                long minLegalTotal = CurrentBet + MinRaiseIncrement;
                if (!isAllIn && total < minLegalTotal)
                {
                    throw new ArgumentOutOfRangeException(nameof(action), total, $"Raise-to {total} is below the minimum legal raise ({minLegalTotal}) and is not an all-in.");
                }

                long payNeeded = total - CommittedStreet[seat];
                long raiseIncrement = total - CurrentBet;
                CommitChips(seat, payNeeded);
                if (raiseIncrement > MinRaiseIncrement)
                {
                    MinRaiseIncrement = raiseIncrement;
                }
                CurrentBet = total;
                LogAction(seat, HeroAction.ActionKind.RaiseTo, total);

                // Any raise (even a short all-in below the full min-raise) reopens
                // action to every other active seat — a disclosed simplification
                // of the casino rule that a short all-in only reopens action for
                // seats that haven't already fully called the prior bet.
                _remainingToAct = Math.Max(0, CountActiveSeats() - (Status[seat] == SeatStatus.Active ? 1 : 0));
                ActingSeat = FindNextActive(seat, inclusive: false);
                break;
            }
        }
        if (_remainingToAct < 0)
        {
            _remainingToAct = 0;
        }
    }

    // ------------------------------------------------------------------
    // The interactive-contract driver loop
    // ------------------------------------------------------------------

    private void RunUntilHeroOrComplete(ref RngState rng)
    {
        while (true)
        {
            if (HandComplete)
            {
                return;
            }
            if (CountLiveNonFolded() <= 1)
            {
                CompleteByFold();
                return;
            }

            if (_remainingToAct > 0)
            {
                int seat = ActingSeat;
                if (IsHero[seat])
                {
                    AwaitingHero = true;
                    return;
                }
                HeroAction action = SeatPolicyOverride[seat] is { } policy
                    ? policy(this, seat, ref rng)
                    : HoldemAgent.DecideAction(this, seat, HoldemProfile.GetArchetype(Archetype[seat]), ref rng);
                ApplyAction(seat, action, ref rng);
                continue;
            }

            // Betting for this street is closed.
            if (CurrentStreet == Street.River)
            {
                CompleteShowdown();
                return;
            }
            AdvanceStreet(ref rng);
        }
    }

    private void AdvanceStreet(ref RngState rng)
    {
        for (int i = 0; i < SeatCount; i++)
        {
            CommittedStreet[i] = 0;
        }
        switch (CurrentStreet)
        {
            case Street.Preflop:
                DealBoardCards(3);
                CurrentStreet = Street.Flop;
                SawFlop = true;
                break;
            case Street.Flop:
                DealBoardCards(1);
                CurrentStreet = Street.Turn;
                break;
            case Street.Turn:
                DealBoardCards(1);
                CurrentStreet = Street.River;
                break;
        }
        BeginBettingRound(0, PostflopFirstToAct());
    }

    private void DealBoardCards(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Board[BoardCount++] = _deck[_deckPos++];
        }
    }

    // ------------------------------------------------------------------
    // Hand resolution
    // ------------------------------------------------------------------

    private long ComputeRake() =>
        SawFlop
            ? Math.Min((long)Math.Round(Pot * HoldemProfile.RakeFraction), (long)Math.Round(HoldemProfile.RakeCapInBigBlinds * BigBlind))
            : 0;

    private void CompleteByFold()
    {
        int winner = -1;
        for (int i = 0; i < SeatCount; i++)
        {
            if (Status[i] != SeatStatus.Folded)
            {
                winner = i;
                break;
            }
        }

        long rake = ComputeRake();
        long payout = Pot - rake;
        Stack[winner] += payout;

        for (int i = 0; i < SeatCount; i++)
        {
            _netChipChangeScratch[i] = (i == winner ? payout : 0) - CommittedTotal[i];
            _showdownScoreScratch[i] = -1;
        }

        Result = new HandResult(_netChipChangeScratch, _showdownScoreScratch, rake, wentToShowdown: false);
        HandComplete = true;
        AwaitingHero = false;
    }

    /// <summary>§8.3's layered side-pot algorithm: distinct commit levels ascending, each layer awarded (net of rake, taken once from the lowest/main layer) to the best eligible HandScore, ties split with the odd chip going to the first eligible seat left of the button.</summary>
    private void CompleteShowdown()
    {
        Span<Card> seven = stackalloc Card[7];
        for (int i = 0; i < SeatCount; i++)
        {
            if (Status[i] == SeatStatus.Folded)
            {
                _showdownScoreScratch[i] = -1;
                continue;
            }
            seven[0] = HoleCards[i * 2];
            seven[1] = HoleCards[i * 2 + 1];
            for (int b = 0; b < BoardCount; b++)
            {
                seven[2 + b] = Board[b];
            }
            _showdownScoreScratch[i] = HoldemEvaluator.EvaluateBest7(seven);
        }

        for (int i = 0; i < SeatCount; i++)
        {
            _netChipChangeScratch[i] = 0; // used as the payout accumulator first, converted to net change below
        }

        Span<long> levels = stackalloc long[MaxSeats];
        int levelCount = 0;
        for (int i = 0; i < SeatCount; i++)
        {
            long c = CommittedTotal[i];
            if (c <= 0)
            {
                continue;
            }
            bool dup = false;
            for (int k = 0; k < levelCount; k++)
            {
                if (levels[k] == c)
                {
                    dup = true;
                    break;
                }
            }
            if (!dup)
            {
                levels[levelCount++] = c;
            }
        }
        for (int i = 1; i < levelCount; i++)
        {
            long key = levels[i];
            int j = i - 1;
            while (j >= 0 && levels[j] > key)
            {
                levels[j + 1] = levels[j];
                j--;
            }
            levels[j + 1] = key;
        }

        long totalRake = ComputeRake();
        long remainingRake = totalRake;
        long previousLevel = 0;
        Span<int> tied = stackalloc int[MaxSeats];

        for (int lvl = 0; lvl < levelCount; lvl++)
        {
            long level = levels[lvl];
            int numAtOrAbove = 0;
            for (int i = 0; i < SeatCount; i++)
            {
                if (CommittedTotal[i] >= level)
                {
                    numAtOrAbove++;
                }
            }
            long layerAmount = (level - previousLevel) * numAtOrAbove;

            // Rake is taken once from the aggregate pot (§8.3), drained from
            // the lowest (main) layer first and spilling into the next layer
            // if the main layer alone is smaller than the nominal rake — this
            // keeps the amount actually deducted always equal to totalRake
            // (Σ layers = Pot ≫ rake), rather than silently under-collecting
            // when a side pot's main layer happens to be small.
            long deduction = Math.Min(remainingRake, layerAmount);
            layerAmount -= deduction;
            remainingRake -= deduction;

            int bestScore = int.MinValue;
            for (int i = 0; i < SeatCount; i++)
            {
                if (CommittedTotal[i] >= level && Status[i] != SeatStatus.Folded && _showdownScoreScratch[i] > bestScore)
                {
                    bestScore = _showdownScoreScratch[i];
                }
            }

            int tiedCount = 0;
            for (int step = 0; step < SeatCount; step++)
            {
                int i = (ButtonSeat + 1 + step) % SeatCount;
                if (CommittedTotal[i] >= level && Status[i] != SeatStatus.Folded && _showdownScoreScratch[i] == bestScore)
                {
                    tied[tiedCount++] = i;
                }
            }

            long share = layerAmount / tiedCount;
            long remainder = layerAmount % tiedCount;
            for (int t = 0; t < tiedCount; t++)
            {
                _netChipChangeScratch[tied[t]] += share + (t < remainder ? 1 : 0);
            }
            previousLevel = level;
        }

        for (int i = 0; i < SeatCount; i++)
        {
            Stack[i] += _netChipChangeScratch[i];
            _netChipChangeScratch[i] -= CommittedTotal[i];
        }

        long actualRake = totalRake - remainingRake;
        Result = new HandResult(_netChipChangeScratch, _showdownScoreScratch, actualRake, wentToShowdown: true);
        HandComplete = true;
        AwaitingHero = false;
    }
}
