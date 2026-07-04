using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// What the UI sees while the human's PA is live: the fixed PA context, the
/// evolving count, and — when batting — the v4 pre-pitch look (type cue +
/// scouting zone probability) the zone-read minigame is played against. When
/// <see cref="IsPitching"/> is set the human is on the mound and the UI must
/// answer with a pitch call instead of a swing/take. Published by the sim
/// thread before every interactive pitch; the UI thread polls it and renders
/// (dirty-flag on the bridge).
/// </summary>
public readonly struct AtBatSnapshot
{
    public readonly HumanPaContext Context;
    public readonly int Balls;
    public readonly int Strikes;
    public readonly PitchLook Look;
    public readonly bool IsPitching;

    public AtBatSnapshot(in HumanPaContext context, int balls, int strikes, in PitchLook look, bool isPitching)
    {
        Context = context;
        Balls = balls;
        Strikes = strikes;
        Look = look;
        IsPitching = isPitching;
    }
}

/// <summary>
/// One NPC plate appearance for the attended-game play-by-play feed (the
/// "render NPC PAs between the avatar's at-bats" gap) — a display name, not a
/// player_id, since the feed is a UI-only convenience and never round-trips
/// back into the sim. Published only when <see cref="MicroGame.FeedSink"/> is
/// attached (i.e. an interactive game with a live viewer); the macro/harness
/// hot paths never set it, so this never touches the zero-GC contract.
/// <see cref="IsRivalryPa"/> carries the same per-PA rivalry bit
/// <see cref="MicroGame"/> already computed for the Phase 6 ratings boost —
/// no extra lookup, just the existing byte forwarded as a bool.
/// </summary>
public readonly struct NpcPaFeedEvent
{
    public readonly string BatterName;
    public readonly PaOutcome Outcome;
    public readonly int Inning;
    public readonly bool IsTopHalf;
    public readonly int Runs;
    public readonly bool IsRivalryPa;

    public NpcPaFeedEvent(string batterName, PaOutcome outcome, int inning, bool isTopHalf, int runs, bool isRivalryPa)
    {
        BatterName = batterName;
        Outcome = outcome;
        Inning = inning;
        IsTopHalf = isTopHalf;
        Runs = runs;
        IsRivalryPa = isRivalryPa;
    }
}

/// <summary>
/// Thread handshake between the attended-game sim (a background task blocked
/// inside <see cref="PitchChain.SimulatePa"/>) and the Godot main thread.
/// Sim side publishes a snapshot and waits; UI side renders, then submits the
/// player's swing/take + zone guess (batting) or pitch call (pitching), which
/// releases the sim for one pitch. Interactive PAs are ~5 per game, so this
/// path is not zero-GC constrained — clarity over allocation here; the NPC
/// hot path never touches it. The NPC play-by-play feed (below) rides the
/// same non-hot-path allowance: dozens of events per game, only ever queued
/// when a UI has attached itself as <see cref="MicroGame.FeedSink"/>.
///
/// Engine-independent (System.Threading only) so the harness can prove the
/// handshake headless with a scripted UI thread.
/// </summary>
public sealed class PlayerIntentBridge
{
    private enum AwaitKind : byte
    {
        None,
        BatterIntent,
        PitchCall,
    }

    private readonly object _gate = new();
    private readonly SemaphoreSlim _intentReady = new(0, 1);
    private readonly Queue<PaOutcome> _resolvedPas = new(8);
    private readonly Queue<NpcPaFeedEvent> _npcFeed = new(32);

    private AtBatSnapshot _snapshot;
    private bool _snapshotDirty;
    private AwaitKind _awaiting;
    private bool _intentIsSwing;
    private double _intentTiming;
    private bool _intentGuessInZone;
    private PitchType _intentPitchType;
    private bool _intentTargetInZone;
    private bool _cancelled;

    // ------------------------------------------------------------------
    // Sim-thread side (called from inside the policies)
    // ------------------------------------------------------------------

    /// <summary>
    /// Publishes the pre-pitch snapshot and blocks until the UI submits a
    /// swing/take + zone guess (or <see cref="Cancel"/> fires — surfaced as
    /// <see cref="OperationCanceledException"/> so the game unwinds unflushed).
    /// </summary>
    internal void AwaitIntent(in AtBatSnapshot snapshot, out bool isSwing, out double timing, out bool guessInZone)
    {
        BeginAwait(in snapshot, AwaitKind.BatterIntent);
        _intentReady.Wait();
        lock (_gate)
        {
            ThrowIfCancelled();
            isSwing = _intentIsSwing;
            timing = _intentTiming;
            guessInZone = _intentGuessInZone;
        }
    }

    /// <summary>Pitcher-side twin: blocks until the UI calls the pitch (type + zone target).</summary>
    internal void AwaitPitchCall(in AtBatSnapshot snapshot, out PitchType type, out bool targetInZone)
    {
        BeginAwait(in snapshot, AwaitKind.PitchCall);
        _intentReady.Wait();
        lock (_gate)
        {
            ThrowIfCancelled();
            type = _intentPitchType;
            targetInZone = _intentTargetInZone;
        }
    }

    internal void PublishPaResolved(PaOutcome outcome)
    {
        lock (_gate)
        {
            _resolvedPas.Enqueue(outcome);
        }
    }

    /// <summary>Called by <see cref="MicroGame"/> at the PA boundary for every NPC plate appearance.</summary>
    internal void PublishNpcPa(in NpcPaFeedEvent evt)
    {
        lock (_gate)
        {
            _npcFeed.Enqueue(evt);
        }
    }

    private void BeginAwait(in AtBatSnapshot snapshot, AwaitKind kind)
    {
        lock (_gate)
        {
            ThrowIfCancelled();
            _snapshot = snapshot;
            _snapshotDirty = true;
            _awaiting = kind;
        }
    }

    private void ThrowIfCancelled()
    {
        if (_cancelled)
        {
            throw new OperationCanceledException("Attended game cancelled by the UI.");
        }
    }

    // ------------------------------------------------------------------
    // UI-thread side
    // ------------------------------------------------------------------

    /// <summary>True while the sim is blocked waiting for the player (swing/take or pitch call).</summary>
    public bool IsAwaitingIntent
    {
        get { lock (_gate) { return _awaiting != AwaitKind.None; } }
    }

    /// <summary>True once per published snapshot — poll from the driver's _Process.</summary>
    public bool TryGetSnapshot(out AtBatSnapshot snapshot)
    {
        lock (_gate)
        {
            snapshot = _snapshot;
            bool dirty = _snapshotDirty;
            _snapshotDirty = false;
            return dirty;
        }
    }

    /// <summary>Dequeues one resolved human PA for the play log, if any.</summary>
    public bool TryDequeuePaOutcome(out PaOutcome outcome)
    {
        lock (_gate)
        {
            if (_resolvedPas.Count > 0)
            {
                outcome = _resolvedPas.Dequeue();
                return true;
            }
            outcome = default;
            return false;
        }
    }

    /// <summary>Dequeues one NPC play-by-play line for the attended-game feed, if any.</summary>
    public bool TryDequeueNpcPa(out NpcPaFeedEvent evt)
    {
        lock (_gate)
        {
            if (_npcFeed.Count > 0)
            {
                evt = _npcFeed.Dequeue();
                return true;
            }
            evt = default;
            return false;
        }
    }

    /// <summary>Swing with a zone read: guessInZone is the player's in/out call for this pitch.</summary>
    public void SubmitSwing(double timingError, bool guessInZone) =>
        SubmitBatter(isSwing: true, timingError, guessInZone);

    public void SubmitTake(bool guessInZone) => SubmitBatter(isSwing: false, 0.0, guessInZone);

    /// <summary>Pitch call while the human is on the mound: pitch type + aim in/out of the zone.</summary>
    public void SubmitPitchCall(PitchType type, bool targetInZone)
    {
        lock (_gate)
        {
            if (_awaiting != AwaitKind.PitchCall || _cancelled)
            {
                return; // stale click — no pitch call is pending
            }
            _awaiting = AwaitKind.None;
            _intentPitchType = type;
            _intentTargetInZone = targetInZone;
            _intentReady.Release();
        }
    }

    /// <summary>
    /// Aborts the attended game: the blocked sim thread throws
    /// OperationCanceledException out of PlayGame and nothing is flushed.
    /// </summary>
    public void Cancel()
    {
        lock (_gate)
        {
            if (_cancelled)
            {
                return;
            }
            _cancelled = true;
            if (_awaiting != AwaitKind.None)
            {
                _awaiting = AwaitKind.None;
                _intentReady.Release();
            }
        }
    }

    /// <summary>Fresh state for the next attended game (clears any cancel/leftovers).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _cancelled = false;
            _snapshotDirty = false;
            _awaiting = AwaitKind.None;
            _resolvedPas.Clear();
            _npcFeed.Clear();
            while (_intentReady.CurrentCount > 0)
            {
                _intentReady.Wait(0);
            }
        }
    }

    private void SubmitBatter(bool isSwing, double timingError, bool guessInZone)
    {
        lock (_gate)
        {
            if (_awaiting != AwaitKind.BatterIntent || _cancelled)
            {
                return; // stale click — no pitch is pending
            }
            // Consumed: a second click before the sim wakes must not release
            // the semaphore twice and pre-answer the NEXT pitch.
            _awaiting = AwaitKind.None;
            _intentIsSwing = isSwing;
            _intentTiming = Math.Clamp(timingError, -1.0, 1.0);
            _intentGuessInZone = guessInZone;
            _intentReady.Release();
        }
    }
}

/// <summary>
/// The human batting policy: forwards each pending pitch — count plus the v4
/// look (blurred type cue + scouting zone probability) — to the UI through a
/// <see cref="PlayerIntentBridge"/>, blocks for the player's swing/take + zone
/// guess, and hands the raw intent to the chain, which resolves the guess
/// against the actual drawn location (the real zone-read minigame; the Phase 5
/// coin flip is gone).
/// </summary>
public struct InteractiveBatterPolicy : IBatterPolicy
{
    private readonly PlayerIntentBridge _bridge;
    private HumanPaContext _context;

    public InteractiveBatterPolicy(PlayerIntentBridge bridge)
    {
        _bridge = bridge;
        _context = default;
    }

    public void BeginPa(in HumanPaContext context) => _context = context;

    public BatterIntent NextPitch(in PitchLook look, in CountState count, ref RngState rng)
    {
        _bridge.AwaitIntent(
            new AtBatSnapshot(in _context, count.Balls, count.Strikes, in look, isPitching: false),
            out bool isSwing, out double timing, out bool guessInZone);
        return isSwing
            ? BatterIntent.Swing(timing, guessInZone)
            : BatterIntent.Take(guessInZone);
    }

    public readonly void OnPaResolved(PaOutcome outcome) => _bridge.PublishPaResolved(outcome);
}

/// <summary>
/// The human mound policy (v4 pitcher-side input model): forwards each
/// pending pitch to the UI, blocks for the pitch call (type + zone target),
/// and returns it to the chain, which executes the aim with control-driven
/// accuracy. Engine-side complete; the pitching UI arrives with the pitcher
/// career path (avatar creation flow).
/// </summary>
public struct InteractivePitcherPolicy : IPitcherPolicy
{
    private readonly PlayerIntentBridge _bridge;
    private HumanPaContext _context;

    public InteractivePitcherPolicy(PlayerIntentBridge bridge)
    {
        _bridge = bridge;
        _context = default;
    }

    public void BeginPa(in HumanPaContext context) => _context = context;

    public PitchCall NextPitch(in CountState count, ref RngState rng)
    {
        _bridge.AwaitPitchCall(
            new AtBatSnapshot(in _context, count.Balls, count.Strikes, look: default, isPitching: true),
            out PitchType type, out bool targetInZone);
        return PitchCall.Throw(type, targetInZone);
    }

    public readonly void OnPaResolved(PaOutcome outcome) => _bridge.PublishPaResolved(outcome);
}
