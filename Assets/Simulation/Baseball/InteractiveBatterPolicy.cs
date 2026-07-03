namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// What the UI sees while the human's PA is live: the fixed PA context plus
/// the evolving count. Published by the sim thread before every interactive
/// pitch; the UI thread polls it and renders (dirty-flag on the bridge).
/// </summary>
public readonly struct AtBatSnapshot
{
    public readonly HumanPaContext Context;
    public readonly int Balls;
    public readonly int Strikes;

    public AtBatSnapshot(in HumanPaContext context, int balls, int strikes)
    {
        Context = context;
        Balls = balls;
        Strikes = strikes;
    }
}

/// <summary>
/// Thread handshake between the attended-game sim (a background task blocked
/// inside <see cref="PitchChain.SimulatePa"/>) and the Godot main thread.
/// Sim side publishes a snapshot and waits; UI side renders, then submits the
/// player's swing/take, which releases the sim for one pitch. Interactive PAs
/// are ~5 per game, so this path is not zero-GC constrained — clarity over
/// allocation here; the NPC hot path never touches it.
///
/// Engine-independent (System.Threading only) so the harness can prove the
/// handshake headless with a scripted UI thread.
/// </summary>
public sealed class PlayerIntentBridge
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _intentReady = new(0, 1);
    private readonly Queue<PaOutcome> _resolvedPas = new(8);

    private AtBatSnapshot _snapshot;
    private bool _snapshotDirty;
    private bool _awaitingIntent;
    private bool _intentIsSwing;
    private double _intentTiming;
    private bool _cancelled;

    // ------------------------------------------------------------------
    // Sim-thread side (called from inside the policy)
    // ------------------------------------------------------------------

    /// <summary>
    /// Publishes the pre-pitch snapshot and blocks until the UI submits an
    /// intent (or <see cref="Cancel"/> fires — surfaced as
    /// <see cref="OperationCanceledException"/> so the game unwinds unflushed).
    /// </summary>
    internal void AwaitIntent(in AtBatSnapshot snapshot, out bool isSwing, out double timing)
    {
        lock (_gate)
        {
            if (_cancelled)
            {
                throw new OperationCanceledException("Attended game cancelled by the UI.");
            }
            _snapshot = snapshot;
            _snapshotDirty = true;
            _awaitingIntent = true;
        }

        _intentReady.Wait();

        lock (_gate)
        {
            if (_cancelled)
            {
                throw new OperationCanceledException("Attended game cancelled by the UI.");
            }
            isSwing = _intentIsSwing;
            timing = _intentTiming;
        }
    }

    internal void PublishPaResolved(PaOutcome outcome)
    {
        lock (_gate)
        {
            _resolvedPas.Enqueue(outcome);
        }
    }

    // ------------------------------------------------------------------
    // UI-thread side
    // ------------------------------------------------------------------

    /// <summary>True while the sim is blocked waiting for the player's intent.</summary>
    public bool IsAwaitingIntent
    {
        get { lock (_gate) { return _awaitingIntent; } }
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

    public void SubmitSwing(double timingError) => Submit(isSwing: true, timingError);

    public void SubmitTake() => Submit(isSwing: false, 0.0);

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
            if (_awaitingIntent)
            {
                _awaitingIntent = false;
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
            _awaitingIntent = false;
            _resolvedPas.Clear();
            while (_intentReady.CurrentCount > 0)
            {
                _intentReady.Wait(0);
            }
        }
    }

    private void Submit(bool isSwing, double timingError)
    {
        lock (_gate)
        {
            if (!_awaitingIntent || _cancelled)
            {
                return; // stale click — no pitch is pending
            }
            // Consumed: a second click before the sim wakes must not release
            // the semaphore twice and pre-answer the NEXT pitch.
            _awaitingIntent = false;
            _intentIsSwing = isSwing;
            _intentTiming = Math.Clamp(timingError, -1.0, 1.0);
            _intentReady.Release();
        }
    }
}

/// <summary>
/// The Phase 5 human policy: forwards each pending pitch to the UI through a
/// <see cref="PlayerIntentBridge"/>, blocks for the player's swing/take, and
/// maps the raw intent to chain input via <see cref="PlayerInputModel"/>.
///
/// The zone read is drawn 50/50 from the game rng for now — pitch location
/// isn't modeled until the pitch-arsenal schema step (v4), so the read is an
/// honest coin flip rather than a UI guess. Deliberate Phase 5 artifact.
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

    public BatterPitchInput NextPitch(in CountState count, ref RngState rng)
    {
        bool zoneReadCorrect = rng.NextDouble() < 0.5;
        _bridge.AwaitIntent(
            new AtBatSnapshot(in _context, count.Balls, count.Strikes),
            out bool isSwing, out double timing);
        return isSwing
            ? PlayerInputModel.FromSwing(timing, zoneReadCorrect, _context.EffectivePitcher.Stuff)
            : PlayerInputModel.FromTake(zoneReadCorrect);
    }

    public readonly void OnPaResolved(PaOutcome outcome) => _bridge.PublishPaResolved(outcome);
}
