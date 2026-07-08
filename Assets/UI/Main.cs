using DirtAndDiamonds.Core;
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
/// HustleLaunchRequested to ScheduleScreen.SelectWorkActivity (the Plan Today
/// card now lives inside BaseballDashboard, still unreachable from the
/// phone's subtree) and ShopOpenRequested to the EquipmentShopScreen modal (a
/// direct child of Main) — so Main stays the shared ancestor that wires
/// screens without any of them reaching into another's tree.
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

    public override void _Ready()
    {
        _screenContainer = GetNode<Node>("ScreenContainer");
        _equipmentShop = GetNode<EquipmentShopScreen>("EquipmentShopScreen");
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
            // The Plan Today card ships inside the dashboard (12b) but its
            // launch seams stay bridged here, Main being the shared ancestor.
            _scheduleScreen = shell.GetNode<ScheduleScreen>(
                "Margin/Panels/BaseballDashboard/Layout/LeagueRow/ScheduleScreen");
            var phone = shell.GetNode<BurnerPhone>("Margin/Panels/BurnerPhone");
            phone.HustleLaunchRequested += OnHustleLaunchRequested;
            phone.ShopOpenRequested += OnShopOpenRequested;
        }
        else
        {
            _scheduleScreen = null;
            var newGameScreen = (NewGameScreen)NewGameScreenScene.Instantiate();
            newGameScreen.AvatarCreated += OnAvatarCreated;
            _screenContainer.AddChild(newGameScreen);
        }
    }

    private void OnAvatarCreated() => ShowAppropriateScreen();

    private void OnHustleLaunchRequested(int workActivity) =>
        _scheduleScreen?.SelectWorkActivity((WorkActivity)workActivity);

    private void OnShopOpenRequested() => _equipmentShop.Open();
}
