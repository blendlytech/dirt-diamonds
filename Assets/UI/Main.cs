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
/// 10d: also bridges BurnerPhone's HustleLaunchRequested signal to
/// ScheduleScreen.SelectWorkActivity — the two are unrelated siblings
/// (BurnerPhone nested under the shell, ScheduleScreen a direct child of
/// Main), so Main is the shared ancestor that wires them without either
/// reaching into the other's tree.
/// </summary>
public sealed partial class Main : Node
{
    [Export]
    public PackedScene NewGameScreenScene { get; set; } = null!;

    [Export]
    public PackedScene TwoPanelShellScene { get; set; } = null!;

    private Node _screenContainer = null!;
    private ScheduleScreen _scheduleScreen = null!;

    public override void _Ready()
    {
        _screenContainer = GetNode<Node>("ScreenContainer");
        _scheduleScreen = GetNode<ScheduleScreen>("ScheduleScreen");
        ShowAppropriateScreen();
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
            var phone = shell.GetNode<BurnerPhone>("Panels/BurnerPhone");
            phone.HustleLaunchRequested += OnHustleLaunchRequested;
        }
        else
        {
            var newGameScreen = (NewGameScreen)NewGameScreenScene.Instantiate();
            newGameScreen.AvatarCreated += OnAvatarCreated;
            _screenContainer.AddChild(newGameScreen);
        }
    }

    private void OnAvatarCreated() => ShowAppropriateScreen();

    private void OnHustleLaunchRequested(int workActivity) =>
        _scheduleScreen.SelectWorkActivity((WorkActivity)workActivity);
}
