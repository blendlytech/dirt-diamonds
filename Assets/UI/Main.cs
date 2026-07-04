using DirtAndDiamonds.Core;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Root of Main.tscn (the project's main scene). Routes boot into either the
/// new-game avatar creation screen or straight to the attended-game screen,
/// depending on whether a career already exists. GameManager is an autoload
/// so it boots (and restores any saved avatar) before this scene's _Ready
/// runs, making Career.HasAvatar reliable here. Swapping only touches
/// ScreenContainer's children — EventChoiceScreen/SuccessionScreen are
/// permanent siblings (declared in Main.tscn, always instantiated, each
/// self-hiding via its own Visible flag) that must never be freed by a
/// screen swap.
/// </summary>
public sealed partial class Main : Node
{
    [Export]
    public PackedScene NewGameScreenScene { get; set; } = null!;

    [Export]
    public PackedScene AttendedGameScreenScene { get; set; } = null!;

    private Node _screenContainer = null!;

    public override void _Ready()
    {
        _screenContainer = GetNode<Node>("ScreenContainer");
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
            _screenContainer.AddChild(AttendedGameScreenScene.Instantiate());
        }
        else
        {
            var newGameScreen = (NewGameScreen)NewGameScreenScene.Instantiate();
            newGameScreen.AvatarCreated += OnAvatarCreated;
            _screenContainer.AddChild(newGameScreen);
        }
    }

    private void OnAvatarCreated() => ShowAppropriateScreen();
}
