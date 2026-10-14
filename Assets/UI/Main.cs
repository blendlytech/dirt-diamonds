using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Economy.Hustles;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Root of Main.tscn (the project's main scene). Routes boot into either the
/// new-game avatar creation screen or straight to the two-panel shell (the
/// Phase 10 career home: Baseball Dashboard | Burner Phone), depending on
/// whether a career already exists. GameManager is an autoload so it boots
/// (and restores any saved avatar) before this scene's _Ready runs, making
/// Career.HasAvatar reliable here. Swapping only touches ScreenContainer's
/// children — SuccessionScreen (and every other overlay) is a permanent
/// sibling (declared in Main.tscn, always instantiated, self-hiding via its
/// own Visible flag) that must never be freed by a screen swap.
/// EventChoiceScreen retired in Phase 10b — the Burner Phone's pending-choice
/// thread renders it now (presentation_layer_narrative.md §4.4).
/// 10d/12b: also bridges BurnerPhone's launch signals across subtrees —
/// HustleLaunchRequested to ScheduleScreen.SelectWorkActivity and
/// ShopOpenRequested to the EquipmentShopScreen modal (a direct child of
/// Main) — so Main stays the shared ancestor that wires screens without any
/// of them reaching into another's tree. UI-reorg: the Plan Today card
/// (ScheduleScreen) now lives inside the phone's Calendar tab, not the
/// dashboard — Main still bridges the signal to it since it's the shared
/// ancestor of both subtrees.
/// Onboarding-2 T-2: TutorialOverlay is the same permanent-sibling pattern as
/// EquipmentShopScreen. Main is also the rect-resolution bridge for its
/// spotlight (onboarding_tutorial_overlay.md §3.2) — the overlay never
/// resolves a BurnerPhone/BaseballDashboard node itself, it asks Main via
/// TargetRectRequested and Main answers with GetGlobalRect() (or an empty
/// Rect2 if the target isn't currently visible, e.g. the phone is on another
/// tab), and BurnerPhone's Settings tab bridges its Replay button the same
/// way ShopOpenRequested does.
/// </summary>
public sealed partial class Main : Node
{
    [Export]
    public PackedScene NewGameScreenScene { get; set; } = null!;

    [Export]
    public PackedScene TwoPanelShellScene { get; set; } = null!;

    private Node _screenContainer = null!;
    private ScheduleScreen? _scheduleScreen;
    private EquipmentShopScreen _equipmentShop = null!;
    private TutorialOverlay _tutorialOverlay = null!;
    private BurnerPhone? _burnerPhone;
    private BaseballDashboard? _baseballDashboard;

    public override void _Ready()
    {
        _screenContainer = GetNode<Node>("ScreenContainer");
        _equipmentShop = GetNode<EquipmentShopScreen>("EquipmentShopScreen");
        _tutorialOverlay = GetNode<TutorialOverlay>("TutorialOverlay");
        _tutorialOverlay.TargetRectRequested += OnTutorialTargetRectRequested;
        WarnIfProjectThemeMissing();
        ShowAppropriateScreen();
    }

    /// <summary>
    /// The project theme fails to load SILENTLY when the import cache is stale
    /// (a .godot/ that predates the Fonts/ commit can't load the Barlow TTFs,
    /// which makes DirtAndDiamonds.tres unparseable) — the game then boots on
    /// Godot's default gray theme with no other symptom. Pin an unmissable
    /// banner until the project is re-imported. Dev-facing diagnostic, so the
    /// string lives here rather than a scene (same posture as the Steam
    /// degradation log line).
    /// </summary>
    private void WarnIfProjectThemeMissing()
    {
        if (ThemeDB.GetProjectTheme() != null)
        {
            return;
        }

        const string message =
            "THEME FAILED TO LOAD — the game is rendering in Godot's fallback theme. " +
            "Re-import the project (open it in the editor once, or run `godot --headless --import`), then relaunch.";
        GD.PushError(message);

        var banner = new Label
        {
            Text = message,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        banner.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
        banner.AddThemeFontSizeOverride("font_size", 20);
        var layer = new CanvasLayer { Layer = 100 };
        layer.AddChild(banner);
        AddChild(layer);
        banner.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        banner.OffsetBottom = 72.0f;
    }

    private void ShowAppropriateScreen()
    {
        foreach (Node child in _screenContainer.GetChildren())
        {
            child.QueueFree();
        }

        if (GameManager.Instance!.Career.HasAvatar)
        {
            Node shell = TwoPanelShellScene.Instantiate();
            _screenContainer.AddChild(shell);
            // The Plan Today card ships inside the phone's Calendar tab
            // (UI-reorg) but its launch seams stay bridged here, Main being
            // the shared ancestor of both the dashboard and the phone.
            _scheduleScreen = shell.GetNode<ScheduleScreen>(
                "Margin/ShellLayout/Panels/BurnerPhone/Screen/ScreenLayout/PhoneTabs/Calendar/CalendarScroll/CalendarTabLayout/ScheduleScreen");
            var phone = shell.GetNode<BurnerPhone>("Margin/ShellLayout/Panels/BurnerPhone");
            phone.HustleLaunchRequested += OnHustleLaunchRequested;
            phone.ShopOpenRequested += OnShopOpenRequested;
            phone.TutorialReplayRequested += OnTutorialReplayRequested;
            _burnerPhone = phone;
            _baseballDashboard = shell.GetNode<BaseballDashboard>("Margin/ShellLayout/Panels/BaseballDashboard");
            TryOpenTutorial();
        }
        else
        {
            _scheduleScreen = null;
            _burnerPhone = null;
            _baseballDashboard = null;
            var newGameScreen = (NewGameScreen)NewGameScreenScene.Instantiate();
            newGameScreen.AvatarCreated += OnAvatarCreated;
            _screenContainer.AddChild(newGameScreen);
        }
    }

    private void OnAvatarCreated() => ShowAppropriateScreen();

    private void OnHustleLaunchRequested(int workActivity) =>
        _scheduleScreen?.SelectWorkActivity((WorkActivity)workActivity);

    private void OnShopOpenRequested() => _equipmentShop.Open();

    private void OnTutorialReplayRequested() => _tutorialOverlay.Open(0);

    /// <summary>
    /// onboarding_tutorial_overlay.md §3.3/§7 risk 2: opens the walkthrough
    /// only after the shell exists and only on the HasAvatar branch — this is
    /// reached from both spec call sites (the tail of _Ready when an avatar
    /// already exists, and the tail of OnAvatarCreated for a brand-new one),
    /// since both funnel through this single HasAvatar branch of
    /// ShowAppropriateScreen. Save-compat guard: an absent tutorial_step key
    /// on a save already past day 2 is a pre-slice save, not an unstarted
    /// tutorial — mark it done and never show the overlay to a veteran
    /// mid-career.
    /// </summary>
    private void TryOpenTutorial()
    {
        GameManager gm = GameManager.Instance!;
        bool present = gm.GameState.TryGetInt64(GameStateKeys.TutorialStep, out long step);
        if (!present)
        {
            if (gm.State.CurrentDay > 2)
            {
                gm.GameState.SetInt64(GameStateKeys.TutorialStep, -1);
                return;
            }
            step = 0;
        }
        if (step < 0)
        {
            return;
        }
        // Persisted value is 1-based (GameStateKeys.TutorialStep: "1..N = the
        // next step index to present"), matching the overlay's 1-based "Step
        // N of 8" display; Open() takes a 0-based array index, so translate
        // at this boundary. step == 0 (never shown) yields -1, which Open()
        // clamps back to 0 — the same first-step landing as a fresh save.
        _tutorialOverlay.Open((int)step - 1);
    }

    /// <summary>
    /// The overlay's spotlight bridge (§3.2): resolves the requested target
    /// against the dashboard/phone subtrees Main already owns and answers
    /// with its current screen rect, or an empty Rect2 if the target isn't
    /// resolvable right now (wrong phone tab, avatar lost mid-tutorial) so
    /// the overlay degrades to a centered, unhighlighted card.
    /// </summary>
    private void OnTutorialTargetRectRequested(int target)
    {
        Control? resolved = (TutorialTarget)target switch
        {
            TutorialTarget.TimeBar =>
                _baseballDashboard?.GetNodeOrNull<Control>("Layout/HeaderBand/HeaderRow/TimeControlBar"),
            TutorialTarget.PhoneTabs =>
                _burnerPhone?.GetNodeOrNull<Control>("Screen/ScreenLayout/PhoneTabs"),
            TutorialTarget.PlanToday => _scheduleScreen,
            TutorialTarget.NeedsCard =>
                _burnerPhone?.GetNodeOrNull<Control>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard"),
            TutorialTarget.BankFunds =>
                _burnerPhone?.GetNodeOrNull<Control>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/FundsCard"),
            TutorialTarget.SettingsSave =>
                _burnerPhone?.GetNodeOrNull<Control>("Screen/ScreenLayout/PhoneTabs/Settings/SettingsScroll/SettingsLayout/SaveCard"),
            _ => null,
        };
        Rect2 rect = resolved is not null && resolved.IsVisibleInTree() ? resolved.GetGlobalRect() : default;
        _tutorialOverlay.SetTargetRect(rect);
    }
}
