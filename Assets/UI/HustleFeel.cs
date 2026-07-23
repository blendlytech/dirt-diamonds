using System;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// P0 "Hustle Feel Pass" helpers, shared by the three crime screens
/// (<see cref="RobberyScreen"/>, <see cref="NarcoticsHustleScreen"/>,
/// <see cref="FencingScreen"/>): tweened panel fades, odds-tinting, and the
/// staged result reveal (<see cref="ResultReveal"/>). Everything here is
/// presentation over already-resolved state — no resolver call, no RNG draw,
/// no DB — so the HustleHarness bands stay byte-identical by construction.
/// All tweens are modulate-only (container-safe under VBox layout) and run on
/// event-driven paths, never per-frame.
/// </summary>
public static class HustleFeel
{
    public const double PanelFadeSec = 0.18;
    public const double CountUpSec = 0.9;
    public const double LinePauseSec = 0.45;

    /// <summary>Fades a freshly shown panel in. Modulate-only so VBox layout is never fought.</summary>
    public static void FadeIn(Control panel)
    {
        Color start = panel.Modulate;
        start.A = 0f;
        panel.Modulate = start;
        panel.CreateTween().TweenProperty(panel, "modulate:a", 1f, PanelFadeSec);
    }

    /// <summary>Tint for a "chance this goes right" number: green when comfortable, amber when tight, red when reckless.</summary>
    public static Color OddsColor(double successProbability) =>
        successProbability >= 0.65 ? UiColors.Success
        : successProbability >= 0.40 ? UiColors.Warning
        : UiColors.Danger;

    /// <summary>Tint for a "chance this goes wrong" number — the same bands, inverted.</summary>
    public static Color RiskColor(double riskProbability) => OddsColor(1.0 - riskProbability);
}

/// <summary>
/// Fluent builder for the staged result reveal: the headline lands with its
/// sting, the take counts up, consequences drop line-by-line, and only then do
/// the footer buttons appear. Each builder call hides its control immediately
/// and queues the tween step that reveals it, so the result panel opens empty
/// and fills in real time. The owning screen keeps the instance and must
/// <see cref="Kill"/> it whenever the session restarts or evaporates
/// mid-reveal. Allocation is per-result (an event-driven UI moment), never
/// per-frame — outside the zero-GC mandate's simulation loops.
/// </summary>
public sealed class ResultReveal
{
    private readonly Tween _tween;

    private ResultReveal(Tween tween) => _tween = tween;

    /// <summary>Starts a reveal sequenced on <paramref name="panel"/>. The panel's own fade is the caller's ShowPanel concern.</summary>
    public static ResultReveal Begin(Control panel) => new(panel.CreateTween());

    /// <summary>Immediately hides controls this result doesn't use (a stale take label, a re-offer button that's out of runs).</summary>
    public ResultReveal Clear(params Control[] controls)
    {
        foreach (Control control in controls)
        {
            control.Visible = false;
        }
        return this;
    }

    public ResultReveal Headline(Label label, string text, Color tint, UiSound sting)
    {
        label.Visible = false;
        _tween.TweenCallback(Callable.From(() =>
        {
            label.Text = text;
            label.AddThemeColorOverride("font_color", tint);
            label.Visible = true;
            UiSfx.Instance.Play(sting);
        }));
        return this;
    }

    /// <summary>The take counts up from zero to <paramref name="amount"/>, formatted through <paramref name="format"/> each step.</summary>
    public ResultReveal CountUp(Label label, string format, double amount)
    {
        label.Visible = false;
        _tween.TweenCallback(Callable.From(() => label.Visible = true));
        _tween.TweenMethod(
            Callable.From((double v) => label.Text = string.Format(format, v)),
            0.0, amount, HustleFeel.CountUpSec);
        return this;
    }

    /// <summary>Queues one consequence line; null/empty text hides the slot and queues nothing.</summary>
    public ResultReveal Line(Label slot, string? text, UiSound sting = UiSound.DayTick)
    {
        slot.Visible = false;
        if (string.IsNullOrEmpty(text))
        {
            return this;
        }

        _tween.TweenInterval(HustleFeel.LinePauseSec);
        _tween.TweenCallback(Callable.From(() =>
        {
            slot.Text = text;
            slot.Visible = true;
            UiSfx.Instance.Play(sting);
        }));
        return this;
    }

    /// <summary>Footer controls (Done / re-offer / tally) stay hidden until the whole reveal has landed, then fade in together.</summary>
    public ResultReveal Footer(params Control[] controls)
    {
        foreach (Control control in controls)
        {
            control.Visible = false;
        }

        _tween.TweenInterval(HustleFeel.LinePauseSec);
        _tween.TweenCallback(Callable.From(() =>
        {
            foreach (Control control in controls)
            {
                control.Visible = true;
                HustleFeel.FadeIn(control);
            }
        }));
        return this;
    }

    /// <summary>Stops the reveal mid-flight (new run, session evaporated, Done). Safe after completion.</summary>
    public void Kill() => _tween.Kill();
}
