using System;
using System.Collections.Generic;
using System.Threading;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Narrative.Events;

/// <summary>
/// The Gritty Event dispatcher (BUILD_PLAN Phase 7): a background polling
/// loop over the Entity_Flags and Players tables, per
/// .claude/rules/gritty_events.md. Design: gritty_event_framework.md §1/§6.
///
/// Threading: the loop runs on its own thread against the read-only WAL
/// companion connection (<see cref="DatabaseManager.ReadOnlyView"/> via
/// <see cref="NarrativePollQueries"/>) — it polls while the sims write and
/// never writes itself. Fires are published with <see cref="EventBus.Publish"/>
/// (thread-safe by Phase-2 design, built for this exact caller); the
/// consequence applier consumes them on the main pump.
///
/// Steady state is one prepared current_day read per interval; the full
/// snapshot + evaluation sweep runs only when the day moves. The first
/// observed day is recorded, not evaluated — the dispatcher reacts to day
/// advancement, never to boot, so a reload cannot re-roll a day already
/// lived. Multi-day jumps (autopilot fast-forward) evaluate each missed day
/// in order against the current snapshot (per-day probability preserved;
/// prerequisites approximated by present state — disclosed in the doc).
///
/// RngState is folder-homed in Simulation/Baseball but is pure math with no
/// baseball dependency; the Life↔Baseball boundary does not apply to
/// Narrative (orchestration layer, like GameManager).
/// </summary>
public sealed class EventDispatcher : IDisposable
{
    /// <summary>Per-evaluated-day safety valve: a content mistake degrades to a noisy day, not 136 events.</summary>
    public const int MaxFiresPerDay = 8;

    public const int DefaultPollIntervalMs = 250;

    private readonly GrittyEventLibrary _library;
    private readonly NarrativePollQueries _poll;
    private readonly EventBus _bus;
    private readonly int _pollIntervalMs;

    // Thread-confined to whichever caller drives evaluation: the poller
    // thread once Start() runs, or the harness calling EvaluateDay directly.
    private RngState _rng;

    // Reused snapshot buffers — rebuilt once per evaluated day, never per poll.
    private readonly List<PollPlayerRow> _players = new(160);
    private readonly Dictionary<(string PlayerId, string FlagName), long> _activeFlags = new();

    // (event index, subject) -> last fired day, for cooldown pacing. In-memory
    // by design (§2: pacing, not state — permanence is content's flag round-trip).
    private readonly Dictionary<(int EventIndex, string SubjectId), long> _lastFiredDay = new();

    private readonly ManualResetEventSlim _stopSignal = new(false);
    private Thread? _thread;
    private long _lastEvaluatedDay = -1;
    private volatile bool _stopping;
    private bool _disposed;

    /// <summary>Total events fired since construction (all threads' view is eventually consistent; exact under the harness's synchronous drive).</summary>
    public long TotalFired { get; private set; }

    public EventDispatcher(
        GrittyEventLibrary library, NarrativePollQueries pollQueries, EventBus bus,
        ulong rngSeed, int pollIntervalMs = DefaultPollIntervalMs)
    {
        _library = library;
        _poll = pollQueries;
        _bus = bus;
        _rng = new RngState(rngSeed | 1UL);
        _pollIntervalMs = Math.Max(1, pollIntervalMs);
    }

    /// <summary>
    /// Starts the background polling thread. Idempotent. The current day at
    /// start is recorded as already-lived — only subsequent advancement
    /// evaluates.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread is not null)
        {
            return;
        }

        if (_poll.TryGetStateInteger(GameStateKeys.CurrentDay, out long bootDay))
        {
            _lastEvaluatedDay = bootDay;
        }

        _stopping = false;
        _stopSignal.Reset();
        _thread = new Thread(PollLoop)
        {
            IsBackground = true,
            Name = "GrittyEventDispatcher",
        };
        _thread.Start();
    }

    /// <summary>Stops and joins the polling thread. Idempotent; safe before Start.</summary>
    public void Stop()
    {
        _stopping = true;
        _stopSignal.Set();
        _thread?.Join();
        _thread = null;
    }

    private void PollLoop()
    {
        while (!_stopping)
        {
            PollOnce();
            // Wait doubles as the interruptible sleep — Stop() returns promptly
            // instead of waiting out the interval.
            _stopSignal.Wait(_pollIntervalMs);
        }
    }

    /// <summary>
    /// One poll step: the cheap current_day read, then per-day evaluation for
    /// every day the calendar moved since the last one seen. Public for the
    /// harness's allocation-bound check; the thread calls exactly this.
    /// </summary>
    public int PollOnce()
    {
        if (!_poll.TryGetStateInteger(GameStateKeys.CurrentDay, out long currentDay))
        {
            return 0; // save has never ticked — nothing to evaluate yet
        }

        if (_lastEvaluatedDay < 0)
        {
            _lastEvaluatedDay = currentDay;
            return 0;
        }

        int fired = 0;
        while (_lastEvaluatedDay < currentDay && !_stopping)
        {
            fired += EvaluateDay(_lastEvaluatedDay + 1);
            _lastEvaluatedDay++;
        }
        return fired;
    }

    /// <summary>
    /// Evaluates every (event, subject) pairing for one game day: prerequisite
    /// conjunction, cooldown, then the probability roll. At most one fire per
    /// subject per day (first satisfied definition in file order wins) and
    /// <see cref="MaxFiresPerDay"/> overall. Harness-drivable synchronously —
    /// the thread and this method share all state, so never call it while the
    /// thread runs.
    /// </summary>
    public int EvaluateDay(long day)
    {
        _poll.LoadPollPlayers(_players);
        _poll.LoadActiveFlags(_activeFlags);
        bool hasAvatar = _poll.TryGetStateText(GameStateKeys.AvatarPlayerId, out string avatarId);

        IReadOnlyList<GrittyEventDefinition> events = _library.Events;
        int fired = 0;

        for (int p = 0; p < _players.Count && fired < MaxFiresPerDay; p++)
        {
            PollPlayerRow subject = _players[p];

            for (int e = 0; e < events.Count; e++)
            {
                GrittyEventDefinition definition = events[e];

                if (definition.Scope == EventScope.Avatar
                    && (!hasAvatar || !string.Equals(subject.PlayerId, avatarId, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (definition.CooldownDays > 0
                    && _lastFiredDay.TryGetValue((e, subject.PlayerId), out long lastFired)
                    && day - lastFired < definition.CooldownDays)
                {
                    continue;
                }

                if (!ConditionEvaluator.AllHold(definition.Prerequisites, in subject, _activeFlags, day))
                {
                    continue;
                }

                if (_rng.NextDouble() >= definition.Weight)
                {
                    continue;
                }

                _lastFiredDay[(e, subject.PlayerId)] = day;
                _bus.Publish(new GrittyEventFiredEvent(definition.Id, subject.PlayerId, day));
                TotalFired++;
                fired++;
                break; // one fired event per subject per day
            }
        }

        return fired;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        Stop();
        _disposed = true;
        _stopSignal.Dispose();
    }
}
