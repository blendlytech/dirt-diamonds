using Godot;

namespace DirtAndDiamonds.UI.Portraits;

/// <summary>
/// Phase 10e (presentation_layer_narrative.md §6): one portrait slot. Tries
/// <see cref="PortraitLibrary"/> for a pre-generated asset keyed by
/// player_id (avatar/heirs) or portrait_key (contacts); when none exists —
/// every identity today — renders the deterministic procedural fallback
/// instead (<see cref="PortraitTile"/>'s themed initials-on-color tile), so
/// no missing-image box ever renders. Lazy: <see cref="SetIdentity"/> is a
/// no-op unless the key actually changed, so callers can invoke it from
/// their own dirty-flag refresh points every time without rebuilding
/// per-frame. Safe pre-tree too: callers that instantiate this scene and set
/// the identity before parenting it (the phone's pending event card) get the
/// apply deferred to <see cref="_Ready"/> instead of a null-node crash.
/// Node paths verified against PortraitView.tscn (authored
/// alongside this script) via godot_scene_mapper before wiring.
/// </summary>
public sealed partial class PortraitView : PanelContainer
{
    [Export]
    public Color[] Palette { get; set; } =
    {
        new Color(0.29f, 0.36f, 0.55f), // slate blue
        new Color(0.48f, 0.29f, 0.55f), // plum
        new Color(0.55f, 0.42f, 0.20f), // ochre
        new Color(0.20f, 0.48f, 0.55f), // teal-blue
        new Color(0.42f, 0.50f, 0.20f), // olive
        new Color(0.55f, 0.20f, 0.36f), // wine
        new Color(0.20f, 0.55f, 0.42f), // forest-teal
        new Color(0.55f, 0.30f, 0.20f), // rust
    };

    [Export]
    public Color InitialsColor { get; set; } = new(0.918f, 0.902f, 0.875f); // theme ink

    private TextureRect _textureRect = null!;
    private Label _initialsLabel = null!;
    private string? _shownKey;
    private string _shownName = string.Empty;

    public override void _Ready()
    {
        _textureRect = GetNode<TextureRect>("TextureRect");
        _initialsLabel = GetNode<Label>("InitialsCenter/InitialsLabel");
        if (_shownKey is not null)
        {
            Apply(_shownKey, _shownName);
        }
    }

    /// <summary>Shows <paramref name="key"/>'s portrait (real art if authored, else the procedural fallback). No-op if <paramref name="key"/> is unchanged since the last call. Callable before the node enters the tree — the apply then waits for <see cref="_Ready"/>.</summary>
    public void SetIdentity(string key, string displayName)
    {
        if (key == _shownKey)
        {
            return;
        }
        _shownKey = key;
        _shownName = displayName;
        if (IsNodeReady())
        {
            Apply(key, displayName);
        }
    }

    private void Apply(string key, string displayName)
    {
        if (PortraitLibrary.TryLoad(key, out Texture2D texture))
        {
            _textureRect.Texture = texture;
            _textureRect.Visible = true;
            _initialsLabel.Visible = false;
            RemoveThemeStyleboxOverride("panel");
            return;
        }

        _textureRect.Visible = false;
        _initialsLabel.Visible = true;
        _initialsLabel.Text = PortraitTile.Initials(displayName);
        _initialsLabel.AddThemeColorOverride("font_color", InitialsColor);

        int paletteIndex = PortraitTile.PaletteIndex(key) % Palette.Length;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette[paletteIndex],
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusBottomLeft = 6,
        });
    }
}
