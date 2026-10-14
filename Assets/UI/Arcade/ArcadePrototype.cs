using System.Globalization;
using DirtAndDiamonds.Simulation.Arcade;
using Godot;

namespace DirtAndDiamonds.UI.Arcade;

/// <summary>
/// Root of ArcadePrototype.tscn: the G-0b "does it feel good to hit?" scene
/// (docs/tasks/G-0_arcade_feel_prototype_brief.md). One batter (you) against a
/// simple AI pitcher: the pitch comes in, you aim the contact cursor and time
/// the swing, and the ball flies. No fielders, runners, DB, league or career.
///
/// This script only collects input, ticks a <see cref="PrototypeSession"/>
/// (the pure-C# driver that owns the one seam into Assets/Simulation/Arcade),
/// lays out the 320x180 view at an integer scale, and formats the HUD. It
/// writes no game state and never edits the arcade core.
///
/// Controls (registered at runtime so project.godot stays untouched; G-5 moves
/// them into the remappable input map):
///   aim cursor   WASD / left stick       swing        Space / A
///   power swing  Shift / right trigger   tuning panel Tab / Y (pauses the pitch)
///   quit         Esc / Back (closes the panel first)
///
/// Debug only: reached through the launch hook in Main.cs
/// (`godot --path . -- --arcade-proto`, debug builds only) or by running this
/// scene directly. Text in this scene is inline on purpose (a debug tool, not
/// shipped UI); the production G-5 scene reads the strings table.
/// </summary>
public sealed partial class ArcadePrototype : Control
{
    /// <summary>0 = a fresh time-based seed every launch; any other value pins the pitch sequence for repeatable sessions.</summary>
    [Export]
    public int Seed { get; set; }

    private static readonly StringName ActLeft = new("arcade_left");
    private static readonly StringName ActRight = new("arcade_right");
    private static readonly StringName ActUp = new("arcade_up");
    private static readonly StringName ActDown = new("arcade_down");
    private static readonly StringName ActSwing = new("arcade_swing");
    private static readonly StringName ActPower = new("arcade_power");
    private static readonly StringName ActTuning = new("arcade_tuning");
    private static readonly StringName ActQuit = new("arcade_quit");

    private static readonly string[] TierNames = { "High school", "College", "Pro" };
    private static readonly string[] PitchChoiceNames = { "AI mix", "Fastball", "Breaking", "Offspeed" };
    private static readonly string[] HandNames = { "Right", "Left" };

    private PrototypeSettings _settings = null!;
    private PrototypeSession _session = null!;

    private SubViewportContainer _fieldContainer = null!;
    private ArcadeFieldView _field = null!;
    private Label _settingsLabel = null!;
    private Label _tallyLabel = null!;
    private Label _callLabel = null!;
    private Label _detailLabel = null!;
    private Label _timingLabel = null!;
    private Label _bannerLabel = null!;
    private PanelContainer _tuningPanel = null!;
    private VBoxContainer _rows = null!;
    private Label? _seedLabel;
    private Control? _firstFocus;

    private bool _tuningOpen;
    private int _shownVersion = -1;

    public override void _Ready()
    {
        EnsureInputActions();

        _fieldContainer = GetNode<SubViewportContainer>("FieldCenter/FieldContainer");
        _field = GetNode<ArcadeFieldView>("FieldCenter/FieldContainer/FieldViewport/Field");
        _settingsLabel = GetNode<Label>("Hud/PitchPanel/PitchLabel");
        _tallyLabel = GetNode<Label>("Hud/TallyPanel/TallyLabel");
        _callLabel = GetNode<Label>("Hud/ReadoutPanel/ReadoutLayout/CallLabel");
        _detailLabel = GetNode<Label>("Hud/ReadoutPanel/ReadoutLayout/DetailLabel");
        _timingLabel = GetNode<Label>("Hud/ReadoutPanel/ReadoutLayout/TimingLabel");
        _bannerLabel = GetNode<Label>("Hud/BannerLabel");
        _tuningPanel = GetNode<PanelContainer>("TuningPanel");
        _rows = GetNode<VBoxContainer>("TuningPanel/Scroll/Rows");

        _settings = new PrototypeSettings();
        ulong seed = Seed != 0 ? (ulong)Math.Abs((long)Seed) : (ulong)DateTime.UtcNow.Ticks;
        _session = new PrototypeSession(_settings, seed);
        _field.Bind(_session);

        BuildTuningRows();
        Resized += OnResized;
        UpdateScale();
        RefreshSettingsLabel();

        _session.Restart(NowSec());
        RefreshHud(true);
    }

    public override void _ExitTree()
    {
        Resized -= OnResized;
    }

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed(ActQuit))
        {
            if (_tuningOpen)
            {
                CloseTuning();
            }
            else
            {
                GetTree().Quit();
            }
            return;
        }
        if (Input.IsActionJustPressed(ActTuning))
        {
            if (_tuningOpen)
            {
                CloseTuning();
            }
            else
            {
                OpenTuning();
            }
            return;
        }
        if (_tuningOpen)
        {
            return;
        }

        Vector2 stick = Input.GetVector(ActLeft, ActRight, ActDown, ActUp);
        var input = new PrototypeInput(stick.X, stick.Y, Input.IsActionPressed(ActPower), Input.IsActionJustPressed(ActSwing));
        _session.Tick(NowSec(), input);
        RefreshHud(false);
    }

    /// <summary>The monotonic clock the whole prototype runs on (seconds), so swing timing is not quantised to frame deltas.</summary>
    private static double NowSec() => Time.GetTicksUsec() / 1_000_000.0;

    // ------------------------------------------------------------------
    // Input map (runtime-registered: project.godot is out of this slice's scope)
    // ------------------------------------------------------------------

    private static void EnsureInputActions()
    {
        if (InputMap.HasAction(ActSwing))
        {
            return;
        }

        InputMap.AddAction(ActLeft, 0.2f);
        InputMap.AddAction(ActRight, 0.2f);
        InputMap.AddAction(ActUp, 0.2f);
        InputMap.AddAction(ActDown, 0.2f);
        InputMap.AddAction(ActSwing);
        InputMap.AddAction(ActPower);
        InputMap.AddAction(ActTuning);
        InputMap.AddAction(ActQuit);

        BindKey(ActLeft, Key.A);
        BindKey(ActRight, Key.D);
        BindKey(ActUp, Key.W);
        BindKey(ActDown, Key.S);
        BindAxis(ActLeft, JoyAxis.LeftX, -1f);
        BindAxis(ActRight, JoyAxis.LeftX, 1f);
        BindAxis(ActUp, JoyAxis.LeftY, -1f);
        BindAxis(ActDown, JoyAxis.LeftY, 1f);

        BindKey(ActSwing, Key.Space);
        BindPadButton(ActSwing, JoyButton.A);
        BindKey(ActPower, Key.Shift);
        BindAxis(ActPower, JoyAxis.TriggerRight, 1f);
        BindKey(ActTuning, Key.Tab);
        BindPadButton(ActTuning, JoyButton.Y);
        BindKey(ActQuit, Key.Escape);
        BindPadButton(ActQuit, JoyButton.Back);
    }

    private static void BindKey(StringName action, Key key) =>
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });

    private static void BindPadButton(StringName action, JoyButton button) =>
        InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = button });

    private static void BindAxis(StringName action, JoyAxis axis, float value) =>
        InputMap.ActionAddEvent(action, new InputEventJoypadMotion { Axis = axis, AxisValue = value });

    // ------------------------------------------------------------------
    // Layout: a 320x180 view, integer-scaled, nearest-filtered (arcade §10.1)
    // ------------------------------------------------------------------

    private void OnResized() => UpdateScale();

    private void UpdateScale()
    {
        int scale = Math.Max(1, Mathf.FloorToInt(Math.Min(Size.X / ArcadeFieldView.ViewWidth, Size.Y / ArcadeFieldView.ViewHeight)));
        _fieldContainer.StretchShrink = scale;
        _fieldContainer.CustomMinimumSize = new Vector2(ArcadeFieldView.ViewWidth * scale, ArcadeFieldView.ViewHeight * scale);
    }

    // ------------------------------------------------------------------
    // Tuning panel (debug only). Opening it pauses the pitch; closing it starts
    // a fresh one, so a knob always applies to the next pitch.
    // ------------------------------------------------------------------

    private void OpenTuning()
    {
        _tuningOpen = true;
        _tuningPanel.Visible = true;
        _bannerLabel.Text = "Tuning: pitch paused";
        _firstFocus?.GrabFocus();
    }

    private void CloseTuning()
    {
        _tuningOpen = false;
        GetViewport().GuiReleaseFocus();
        _tuningPanel.Visible = false;
        _bannerLabel.Text = string.Empty;
        _session.Restart(NowSec());
    }

    private void BuildTuningRows()
    {
        AddHeading("Pitcher");
        AddOption("Tier", TierNames, (int)_settings.Tier, i => { _settings.Tier = (ArcadeTier)i; RefreshSettingsLabel(); });
        AddOption("Pitch type", PitchChoiceNames, _settings.ForcedPitch + 1, i => { _settings.ForcedPitch = i - 1; RefreshSettingsLabel(); });
        AddSlider("Velocity", 0, 100, 1, _settings.PitcherVelocity, "0", v => { _settings.PitcherVelocity = (int)v; RefreshSettingsLabel(); });
        AddSlider("Movement", 0, 100, 1, _settings.PitcherMovement, "0", v => { _settings.PitcherMovement = (int)v; RefreshSettingsLabel(); });
        AddSlider("Control", 0, 100, 1, _settings.PitcherControl, "0", v => { _settings.PitcherControl = (int)v; RefreshSettingsLabel(); });
        AddSlider("Fatigue multiplier", 0.5, 1.0, 0.05, _settings.PitcherFatigue, "0.00", v => { _settings.PitcherFatigue = v; RefreshSettingsLabel(); });
        AddOption("Throws", HandNames, (int)_settings.PitcherThrows, i => { _settings.PitcherThrows = (Hand)i; });

        AddHeading("Batter");
        AddSlider("Power", 0, 100, 1, _settings.BatterPower, "0", v => { _settings.BatterPower = (int)v; RefreshSettingsLabel(); });
        AddSlider("Contact", 0, 100, 1, _settings.BatterContact, "0", v => { _settings.BatterContact = (int)v; RefreshSettingsLabel(); });
        AddSlider("Discipline (read cue)", 0, 100, 1, _settings.BatterDiscipline, "0", v => { _settings.BatterDiscipline = (int)v; RefreshSettingsLabel(); });
        AddOption("Bats", HandNames, (int)_settings.BatterBats, i => { _settings.BatterBats = (Hand)i; });
        AddCheck("PED active (power x1.5)", _settings.PedActive, on => { _settings.PedActive = on; RefreshSettingsLabel(); });

        AddHeading("Assists (human only)");
        AddSlider("Timing window x", 0.5, 3.0, 0.05, _settings.TimingWindowScale, "0.00", v => { _settings.TimingWindowScale = v; RefreshSettingsLabel(); });
        AddSlider("Cursor size x", 0.5, 3.0, 0.05, _settings.CursorSizeScale, "0.00", v => { _settings.CursorSizeScale = v; RefreshSettingsLabel(); });

        AddHeading("Feel and controls");
        AddSlider("Cursor speed (m/s)", 0.6, 4.0, 0.1, _settings.CursorSpeedMps, "0.0", v => { _settings.CursorSpeedMps = v; });
        AddSlider("Latency comp (ms)", -100, 100, 5, _settings.LatencyCompMs, "0", v => { _settings.LatencyCompMs = v; });
        AddCheck("Reduce motion", _settings.ReduceMotion, on => { _settings.ReduceMotion = on; });

        AddHeading("Session");
        _seedLabel = new Label { Text = SeedText() };
        _rows.AddChild(_seedLabel);
        AddButton("Reseed pitches", OnReseedPressed);
        AddButton("Reset tally", OnResetTallyPressed);
        AddButton("Close (Tab / Y)", CloseTuning);
    }

    private void AddHeading(string text) => _rows.AddChild(new Label { Text = text });

    private void AddSlider(string label, double min, double max, double step, double value, string format, Action<double> apply)
    {
        var row = new HBoxContainer();
        var name = new Label { Text = label, CustomMinimumSize = new Vector2(190, 0) };
        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(120, 0),
        };
        var valueLabel = new Label
        {
            Text = value.ToString(format, CultureInfo.InvariantCulture),
            CustomMinimumSize = new Vector2(52, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        slider.ValueChanged += v =>
        {
            valueLabel.Text = v.ToString(format, CultureInfo.InvariantCulture);
            apply(v);
        };
        row.AddChild(name);
        row.AddChild(slider);
        row.AddChild(valueLabel);
        _rows.AddChild(row);
        _firstFocus ??= slider;
    }

    private void AddOption(string label, string[] items, int selected, Action<int> apply)
    {
        var row = new HBoxContainer();
        var name = new Label { Text = label, CustomMinimumSize = new Vector2(190, 0) };
        var option = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        for (int i = 0; i < items.Length; i++)
        {
            option.AddItem(items[i], i);
        }
        option.Select(selected);
        option.ItemSelected += index => apply((int)index);
        row.AddChild(name);
        row.AddChild(option);
        _rows.AddChild(row);
        _firstFocus ??= option;
    }

    private void AddCheck(string label, bool initial, Action<bool> apply)
    {
        var check = new CheckButton { Text = label, ButtonPressed = initial };
        check.Toggled += on => apply(on);
        _rows.AddChild(check);
    }

    private void AddButton(string label, Action onPressed)
    {
        var button = new Button { Text = label };
        button.Pressed += onPressed;
        _rows.AddChild(button);
    }

    private void OnReseedPressed()
    {
        _session.Reseed((ulong)DateTime.UtcNow.Ticks);
        if (_seedLabel != null)
        {
            _seedLabel.Text = SeedText();
        }
    }

    private void OnResetTallyPressed()
    {
        _session.ResetTally();
        RefreshHud(true);
    }

    private string SeedText() => F($"Seed {_session.Seed}");

    // ------------------------------------------------------------------
    // HUD: text is formatted only when the tally or readout changes, never per frame
    // ------------------------------------------------------------------

    private static string F(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private void RefreshSettingsLabel()
    {
        PrototypeSettings s = _settings;
        string ped = s.PedActive ? "  PED" : string.Empty;
        _settingsLabel.Text = string.Join(
            "\n",
            F($"{TierNames[(int)s.Tier]} | {PitchChoiceNames[s.ForcedPitch + 1]}"),
            F($"Pitcher vel {s.PitcherVelocity}  mov {s.PitcherMovement}  ctl {s.PitcherControl}  fatigue {s.PitcherFatigue:0.00}"),
            F($"Batter pow {s.BatterPower}  con {s.BatterContact}  dis {s.BatterDiscipline}{ped}"),
            F($"Assists: timing x{s.TimingWindowScale:0.00}  cursor x{s.CursorSizeScale:0.00}"));
    }

    private void RefreshHud(bool force)
    {
        int version = _session.ReadoutVersion;
        if (!force && version == _shownVersion)
        {
            return;
        }
        _shownVersion = version;

        PrototypeTally t = _session.Tally;
        PrototypeReadout r = _session.Readout;

        _tallyLabel.Text = string.Join(
            "\n",
            F($"Pitches {t.Pitches}"),
            F($"Swings {t.Swings}   Whiffs {t.Whiffs}"),
            F($"Fouls {t.Fouls}   In play {t.InPlay}"),
            F($"Home runs {t.HomeRuns}"),
            F($"Avg exit velo {t.AverageExitVelocityMph:0.0} mph"));

        _callLabel.Text = CallText(in r);
        _detailLabel.Text = DetailText(in r);
        _timingLabel.Text = TimingText(in r);
    }

    private static string CallText(in PrototypeReadout r)
    {
        if (!r.HasPitch)
        {
            return "Waiting for the first pitch";
        }
        if (r.HasBatted)
        {
            return r.Kind switch
            {
                BattedBallKind.HomeRun => "HOME RUN",
                BattedBallKind.OffTheWall => "Off the wall",
                BattedBallKind.FlyBall => "Fly ball",
                BattedBallKind.LineDrive => "Line drive",
                BattedBallKind.GroundBall => "Ground ball",
                BattedBallKind.PopUp => "Pop-up",
                _ => "Foul ball",
            };
        }
        return r.Call switch
        {
            PitchCall.Ball => "Ball",
            PitchCall.CalledStrike => "Called strike",
            PitchCall.SwingingStrike => "Swing and miss",
            PitchCall.FoulTip => "Foul tip",
            _ => "Foul",
        };
    }

    private string DetailText(in PrototypeReadout r)
    {
        if (!r.HasPitch)
        {
            return string.Empty;
        }

        string pitch = r.Pitch switch
        {
            PitchKind.Fastball => "Fastball",
            PitchKind.Breaking => "Breaking ball",
            _ => "Offspeed",
        };
        string zone = r.Bounced ? "in the dirt" : (r.InZone ? "in the zone" : "outside");
        string text = F($"{pitch}  {r.PitchMph:0} mph  ({zone})");
        if (!r.HasBatted)
        {
            return text;
        }

        // Pull is toward the batter's own side: left field for a right-hander (negative spray).
        double pull = _settings.BatterBats == Hand.Right ? -r.SprayDeg : r.SprayDeg;
        string side = pull > 8.0 ? "pulled" : (pull < -8.0 ? "opposite field" : "up the middle");
        string distanceWord = r.Kind == BattedBallKind.HomeRun ? "Carry" : "Distance";
        return string.Join(
            "\n",
            text,
            F($"Exit velo {r.ExitVelocityMph:0} mph   launch {r.LaunchAngleDeg:0}°   spray {r.SprayDeg:+0;-0;0}° ({side})"),
            F($"{distanceWord} {r.DistanceFt:0} ft   apex {r.ApexFt:0} ft   hang {r.HangTimeSec:0.0} s"));
    }

    private string TimingText(in PrototypeReadout r)
    {
        if (!r.HasPitch)
        {
            return string.Empty;
        }
        if (!r.Swung)
        {
            return "You took it.";
        }

        double ms = r.TimingMs;
        string timing = Math.Abs(ms) < 3.0 ? "on time" : (ms < 0.0 ? F($"early {-ms:0} ms") : F($"late {ms:0} ms"));
        double windowMs = ArcadeTuning.TimingWindowSec((byte)Math.Clamp(_settings.BatterContact, 0, 100), r.PowerSwing)
            * _settings.TimingWindowScale * 1000.0;
        string aim = r.Offset < 0.15 ? "centered" : F($"{r.Offset * 100.0:0}% off-center");
        string swing = r.PowerSwing ? "power swing" : "normal swing";
        string text = F($"Timing {timing} (window +/-{windowMs:0} ms)   Aim {aim}   {swing}");

        if (r.ContactKind == ContactKind.Whiff)
        {
            bool badTiming = Math.Abs(r.TimingFraction) > 1.0;
            bool badAim = r.Offset > 1.0;
            string why = r.Bounced ? "swung at a pitch in the dirt"
                : badTiming && badAim ? "missed on timing and aim"
                : badTiming ? "missed on timing"
                : "missed on aim";
            text += "\n" + why;
        }
        return text;
    }
}
