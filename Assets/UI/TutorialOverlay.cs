using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Which live widget (if any) a tutorial step should spotlight. Resolved to
/// a screen-space Rect2 by <see cref="Main"/> — the shared ancestor of both
/// the dashboard and the phone subtrees — per ui_conventions.md's "never
/// reach into another scene's tree" rule
/// (onboarding_tutorial_overlay.md §3.2). This overlay never resolves a
/// target Control itself.
/// </summary>
public enum TutorialTarget
{
    None = 0,
    TimeBar = 1,
    PhoneTabs = 2,
    PlanToday = 3,
    NeedsCard = 4,
    BankFunds = 5,
    SettingsSave = 6,
}

/// <summary>
/// Day-1 onboarding walkthrough (onboarding_tutorial_overlay.md, T-2): a
/// permanent, self-hiding sibling in Main.tscn — the exact EquipmentShopScreen
/// pattern (scrim + centered Card, never freed by a screen swap). The 8-step
/// script is data, not control flow (<see cref="Steps"/>), and this scene
/// never touches simulation state — it only reads/writes the
/// <see cref="GameStateKeys.TutorialStep"/> checkpoint so a quit mid-tutorial
/// resumes on the same step. Spotlighting a live widget never reaches into
/// another scene's tree directly: this overlay asks Main (via
/// <see cref="TargetRectRequested"/>) for the target's screen rect and
/// degrades to a centered, unhighlighted card when Main answers with an
/// empty Rect2 (target not currently visible — e.g. the phone is on another
/// tab). Node paths verified against TutorialOverlay.tscn via
/// godot_scene_mapper before this script was written.
/// </summary>
public sealed partial class TutorialOverlay : Control
{
    [Signal]
    public delegate void TargetRectRequestedEventHandler(int target);

    private readonly record struct TutorialStep(
        string Title,
        string Body,
        TutorialTarget Target,
        bool ShowNeedsDiagram);

    // Copy is verbatim from onboarding_tutorial_overlay.md §5 (Opus T-0),
    // with markdown emphasis stripped — BodyLabel is a plain Label
    // (AutowrapMode = WordSmart), not RichTextLabel/BBCode; no other
    // phone/dashboard label in this codebase renders BBCode either.
    private static readonly TutorialStep[] Steps =
    {
        new(
            "This is your phone",
            "Everything you do off the field runs through here. Events is where moments land and you answer them. Messages is your texts. Calendar is your season and your day plan. Bank is money and needs. You'll be back here a lot.",
            TutorialTarget.PhoneTabs,
            false),
        new(
            "Time runs on its own",
            "The clock is live. Pause it, run it Slow, Normal or Fast, or hit Skip Day to jump to tomorrow. It pauses itself when something needs you — a choice, a game, a deal.",
            TutorialTarget.TimeBar,
            false),
        new(
            "Five needs, always falling",
            "Open Bank. Hunger, Sleep, Hygiene, Social, Fitness — every one of them drops every hour of every day, and they're listed fastest-first. Hunger falls about eight times quicker than Fitness.",
            TutorialTarget.NeedsCard,
            true),
        new(
            "They fall faster the lower they get",
            "This is the part that kills careers. A need at 30 isn't just worse than one at 60 — it's dropping faster. Neglect compounds. And stress from a bad stretch makes all five fall faster still.",
            TutorialTarget.NeedsCard,
            true),
        new(
            "The line at 20",
            "See the mark near the bottom of each bar? Drop below it and your player stops taking orders. He'll abandon whatever you scheduled and go fix it himself — eat, crash, whatever it takes. You don't want to find out which.",
            TutorialTarget.NeedsCard,
            true),
        new(
            "Free hours are how he survives",
            "Open Calendar → Plan Today. Sleep, School, Practice, Work — you allocate the day. Whatever you don't allocate is Free hours: your player uses them on his own to eat, wash, rest and see people. You never schedule meals — he handles them himself, but only in free time. Book all 24 hours and he has no time to eat. And give him 8 hours of sleep — under 6 or over 8 and he pays for it.",
            TutorialTarget.PlanToday,
            false),
        new(
            "Game days",
            "Games are on your Calendar. When one lands, the Time bar swaps in Play Game — step in and take the at-bats yourself, or Skip Day and let the team handle it. No penalty either way. Player has your stat line afterward; League has the standings.",
            TutorialTarget.TimeBar,
            false),
        new(
            "Money, bills, saving",
            "Bank shows your cash and the weekly cost of living — how much of that your family covers depends on how well-off they are. The game saves itself as you go — there's a Save Now in Settings if you want it, and a Replay tutorial button right under it if you ever want this again. Go play.",
            TutorialTarget.BankFunds,
            false),
    };

    // Static decay-order legend for the three Needs-card steps — the real
    // per-need numbers live in the card's own tooltips (T-1); this is just
    // the ordering already stated in the step body, restated visually while
    // the player is looking at the spotlighted card. No sim read.
    private static readonly string[] NeedsDecayOrder = { "Hunger", "Sleep", "Hygiene", "Social", "Fitness" };

    [Export]
    public string StepCounterFormat { get; set; } = "Step {0} of {1}";

    [Export]
    public string NextButtonText { get; set; } = "Next";

    [Export]
    public string FinishButtonText { get; set; } = "Done";

    private ColorRect _scrimFull = null!;
    private Control _scrimCutout = null!;
    private ColorRect _notchTop = null!;
    private ColorRect _notchBottom = null!;
    private ColorRect _notchLeft = null!;
    private ColorRect _notchRight = null!;
    private Label _stepCounterLabel = null!;
    private Label _titleLabel = null!;
    private Label _bodyLabel = null!;
    private VBoxContainer _diagramContainer = null!;
    private Button _skipButton = null!;
    private Button _backButton = null!;
    private Button _nextButton = null!;

    private int _currentIndex;
    private bool _open;

    public override void _Ready()
    {
        _scrimFull = GetNode<ColorRect>("ScrimFull");
        _scrimCutout = GetNode<Control>("ScrimCutout");
        _notchTop = GetNode<ColorRect>("ScrimCutout/NotchTop");
        _notchBottom = GetNode<ColorRect>("ScrimCutout/NotchBottom");
        _notchLeft = GetNode<ColorRect>("ScrimCutout/NotchLeft");
        _notchRight = GetNode<ColorRect>("ScrimCutout/NotchRight");
        _stepCounterLabel = GetNode<Label>("Card/CardLayout/StepCounterLabel");
        _titleLabel = GetNode<Label>("Card/CardLayout/TitleLabel");
        _bodyLabel = GetNode<Label>("Card/CardLayout/BodyLabel");
        _diagramContainer = GetNode<VBoxContainer>("Card/CardLayout/DiagramContainer");
        _skipButton = GetNode<Button>("Card/CardLayout/ButtonsRow/SkipButton");
        _backButton = GetNode<Button>("Card/CardLayout/ButtonsRow/BackButton");
        _nextButton = GetNode<Button>("Card/CardLayout/ButtonsRow/NextButton");

        _skipButton.Pressed += OnSkipPressed;
        _backButton.Pressed += OnBackPressed;
        _nextButton.Pressed += OnNextPressed;

        ClearHighlight();
    }

    public override void _ExitTree()
    {
        _skipButton.Pressed -= OnSkipPressed;
        _backButton.Pressed -= OnBackPressed;
        _nextButton.Pressed -= OnNextPressed;
    }

    /// <summary>
    /// Opens the walkthrough at <paramref name="fromStep"/> (0-based, clamped
    /// into range). Called by Main on a fresh-save boot (resuming a
    /// checkpointed step) and by the Settings tab's Replay button (always
    /// from 0).
    /// </summary>
    public void Open(int fromStep)
    {
        _currentIndex = Mathf.Clamp(fromStep, 0, Steps.Length - 1);
        _open = true;
        Visible = true;
        // Soft-stop the wall clock while the player reads (the same
        // GameManager seam the hustle/game holds use) — §7's "paused, modal
        // surface" assumption; without it a fresh save burns ~10 game-hours
        // of day 1 at Normal speed during the walkthrough.
        GameManager.Instance!.TutorialOverlayOpen = true;
        ShowStep();
    }

    public override void _Process(double delta)
    {
        if (!_open)
        {
            return;
        }
        // Sanctioned by onboarding_tutorial_overlay.md §7 risk 1: a handful
        // of GetGlobalRect() calls on a paused, modal surface — the
        // ui_conventions no-per-frame rule targets the sim loop, not this.
        // Keeps the spotlight glued to its target across a window resize or
        // a Bank-tab scroll without a separate Resized hook.
        TutorialTarget target = Steps[_currentIndex].Target;
        if (target != TutorialTarget.None)
        {
            EmitSignal(SignalName.TargetRectRequested, (int)target);
        }
    }

    /// <summary>
    /// Main's answer to the last <see cref="TargetRectRequested"/> — an
    /// empty Rect2 means "not visible right now" (wrong phone tab, or no
    /// target for this step), which degrades to a centered card with no
    /// highlight rather than a highlight floating over nothing.
    /// </summary>
    public void SetTargetRect(Rect2 rect)
    {
        if (rect.Size == Vector2.Zero)
        {
            ClearHighlight();
            return;
        }

        _scrimFull.Visible = false;
        _scrimCutout.Visible = true;

        Vector2 viewportSize = GetViewportRect().Size;
        _notchTop.Position = Vector2.Zero;
        _notchTop.Size = new Vector2(viewportSize.X, rect.Position.Y);
        _notchBottom.Position = new Vector2(0f, rect.End.Y);
        _notchBottom.Size = new Vector2(viewportSize.X, viewportSize.Y - rect.End.Y);
        _notchLeft.Position = new Vector2(0f, rect.Position.Y);
        _notchLeft.Size = new Vector2(rect.Position.X, rect.Size.Y);
        _notchRight.Position = new Vector2(rect.End.X, rect.Position.Y);
        _notchRight.Size = new Vector2(viewportSize.X - rect.End.X, rect.Size.Y);
    }

    private void ClearHighlight()
    {
        _scrimFull.Visible = true;
        _scrimCutout.Visible = false;
    }

    private void ShowStep()
    {
        TutorialStep step = Steps[_currentIndex];
        _stepCounterLabel.Text = string.Format(StepCounterFormat, _currentIndex + 1, Steps.Length);
        _titleLabel.Text = step.Title;
        _bodyLabel.Text = step.Body;
        _backButton.Visible = _currentIndex > 0;
        _nextButton.Text = _currentIndex == Steps.Length - 1 ? FinishButtonText : NextButtonText;
        BuildDiagram(step.ShowNeedsDiagram);
        ClearHighlight();
        PersistStep(_currentIndex + 1);
    }

    private void BuildDiagram(bool show)
    {
        foreach (Node child in _diagramContainer.GetChildren())
        {
            child.QueueFree();
        }
        _diagramContainer.Visible = show;
        if (!show)
        {
            return;
        }
        foreach (string needName in NeedsDecayOrder)
        {
            _diagramContainer.AddChild(new Label
            {
                Text = needName,
                ThemeTypeVariation = "CaptionLabel",
            });
        }
    }

    private void OnSkipPressed() => Close();

    private void OnBackPressed()
    {
        if (_currentIndex == 0)
        {
            return;
        }
        _currentIndex--;
        ShowStep();
    }

    private void OnNextPressed()
    {
        if (_currentIndex == Steps.Length - 1)
        {
            Close();
            return;
        }
        _currentIndex++;
        ShowStep();
    }

    private void Close()
    {
        _open = false;
        Visible = false;
        GameManager.Instance!.TutorialOverlayOpen = false;
        PersistStep(-1);
    }

    private static void PersistStep(long step) =>
        GameManager.Instance!.GameState.SetInt64(GameStateKeys.TutorialStep, step);
}
