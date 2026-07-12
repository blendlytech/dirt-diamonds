using System;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// The semantic vocabulary of UI sound roles (ui_audio_slice_d.md §3.4). This
/// enum is the stable contract: callers name the *role*, never the clip. The
/// streams behind each role are synthesized placeholders today
/// (<see cref="UiSfx"/>'s boot-time synth) and recorded assets tomorrow —
/// swapping them touches only the enum→stream table, zero call sites.
/// </summary>
public enum UiSound
{
    /// <summary>Default button press — the workhorse (auto-wired game-wide in D-2).</summary>
    Tap,
    /// <summary>Positive commit: purchase success, Save Now, plan confirmed.</summary>
    Confirm,
    /// <summary>Close / cancel / navigate-back.</summary>
    Back,
    /// <summary>Denied action: insufficient funds, invalid input, blocked advance.</summary>
    Error,
    /// <summary>TabContainer tab change (auto-wired in D-2).</summary>
    TabSwitch,
    /// <summary>Money arrives: sale, hustle payout, funds gained.</summary>
    Cash,
    /// <summary>A gritty event fired / a choice awaits — soft attention chime.</summary>
    Alert,
    /// <summary>The day advanced / a clock milestone — subtle, used sparingly.</summary>
    DayTick,
}

/// <summary>
/// Slice D-1 (ui_audio_slice_d.md §3): the global UI audio service — a second
/// autoload beside GameManager, deliberately a sibling rather than a subsystem
/// property because audio shares nothing with the sim or the save DB. Owns a
/// fixed 8-voice pool of <see cref="AudioStreamPlayer"/>s on the SFX bus
/// (round-robin, zero allocation on the play path per ui_conventions.md's
/// pooling mandate) and the boot-time synthesized placeholder tones — no
/// committed audio assets; real recordings later replace the synth calls in
/// the enum→stream table with zero call-site churn. Volume is a device
/// preference, not world state (§3.5 Option A): it lives in
/// user://settings.cfg, never the save DB, so it survives new games and save
/// wipes.
/// </summary>
public partial class UiSfx : Node
{
    public static UiSfx Instance { get; private set; } = null!;

    /// <summary>Current SFX volume as the slider's 0–100 percent. UiSfx is the single authority; the Settings slider is a read/write view of this number.</summary>
    public float Volume { get; private set; } = DefaultVolume;

    // Pool size is a tuning knob (§3.3): overlapping blips take the next
    // voice; only when all 8 ring at once does the oldest get retasked, which
    // is inaudible for sub-200 ms tones at human click rates.
    private const int VoiceCount = 8;
    private const float DefaultVolume = 100f;
    private const string SfxBusName = "SFX";
    private const string SettingsPath = "user://settings.cfg";
    private const string SettingsSection = "audio";
    private const string SettingsKey = "sfx_volume";
    private const int MixRate = 44100;

    private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[VoiceCount];
    private readonly AudioStream[] _streams = new AudioStream[Enum.GetValues<UiSound>().Length];
    private int _nextVoice;
    private int _sfxBusIndex = -1;
    private bool _volumeDirty;

    private enum Wave { Sine, Square, Triangle }

    public override void _Ready()
    {
        Instance = this;
        // Same reason as GameManager: UI sounds must play through the at-bat
        // freeze and modal pauses — pausing the sims is the sims' concern.
        ProcessMode = ProcessModeEnum.Always;

        // default_bus_layout.tres ships the Master→SFX layout; this guard
        // creates the bus in code if the layout ever goes missing so the
        // service degrades to "still audible", never to a -1 index crash.
        _sfxBusIndex = AudioServer.GetBusIndex(SfxBusName);
        if (_sfxBusIndex < 0)
        {
            _sfxBusIndex = AudioServer.BusCount;
            AudioServer.AddBus(_sfxBusIndex);
            AudioServer.SetBusName(_sfxBusIndex, SfxBusName);
            AudioServer.SetBusSend(_sfxBusIndex, "Master");
            GD.PushWarning("UiSfx: SFX bus missing from default_bus_layout.tres — created in code.");
        }

        SynthesizeStreams();

        for (int i = 0; i < VoiceCount; i++)
        {
            _players[i] = new AudioStreamPlayer { Bus = SfxBusName };
            AddChild(_players[i]);
        }

        LoadVolume();
        GD.Print($"UiSfx ready: {VoiceCount}-voice pool on '{SfxBusName}' bus (index {_sfxBusIndex}), volume {Volume:0}%.");
    }

    public override void _ExitTree()
    {
        // Keyboard-driven slider changes fire ValueChanged without a
        // DragEnded, so flush any unsaved level on shutdown.
        if (_volumeDirty)
        {
            SaveVolume();
        }
    }

    /// <summary>
    /// Plays the clip for <paramref name="sound"/> on the next pooled voice.
    /// Zero-alloc path: table lookup, cursor bump, Play().
    /// </summary>
    public void Play(UiSound sound)
    {
        AudioStreamPlayer voice = _players[_nextVoice];
        _nextVoice = (_nextVoice + 1) % VoiceCount;
        voice.Stream = _streams[(int)sound];
        voice.Play();
    }

    /// <summary>
    /// Applies a 0–100 percent volume to the SFX bus live (heard while
    /// dragging). Does NOT write the config file — callers debounce
    /// persistence to release via <see cref="SaveVolume"/>.
    /// </summary>
    public void SetVolume(float percent)
    {
        Volume = Mathf.Clamp(percent, 0f, 100f);
        if (Volume <= 0f)
        {
            // LinearToDb(0) is -inf; mute is the well-defined zero (§3.2).
            AudioServer.SetBusMute(_sfxBusIndex, true);
        }
        else
        {
            AudioServer.SetBusMute(_sfxBusIndex, false);
            AudioServer.SetBusVolumeDb(_sfxBusIndex, Mathf.LinearToDb(Volume / 100f));
        }

        _volumeDirty = true;
    }

    /// <summary>Persists the current volume to user://settings.cfg (§3.5 Option A — device preference, deliberately not the save DB).</summary>
    public void SaveVolume()
    {
        var config = new ConfigFile();
        config.Load(SettingsPath); // absent file is fine — we overwrite our key and keep any others
        config.SetValue(SettingsSection, SettingsKey, Volume);
        Error err = config.Save(SettingsPath);
        if (err != Error.Ok)
        {
            GD.PushWarning($"UiSfx: failed to save {SettingsPath} ({err}).");
        }

        _volumeDirty = false;
    }

    private void LoadVolume()
    {
        var config = new ConfigFile();
        float loaded = DefaultVolume;
        if (config.Load(SettingsPath) == Error.Ok)
        {
            loaded = (float)config.GetValue(SettingsSection, SettingsKey, DefaultVolume);
        }

        SetVolume(loaded);
        _volumeDirty = false; // just loaded — nothing to flush
    }

    /// <summary>
    /// Boot-time placeholder synthesis (§6): one distinct, legible timbre per
    /// role, ~40–220 ms enveloped blips baked into AudioStreamWav once. These
    /// are explicitly disposable — real recorded assets replace these calls
    /// and nothing downstream changes.
    /// </summary>
    private void SynthesizeStreams()
    {
        _streams[(int)UiSound.Tap] = MakeTone(new[] { 1200f }, 40, Wave.Sine, 0.45f);
        _streams[(int)UiSound.Confirm] = MakeTone(new[] { 660f, 880f }, 120, Wave.Sine, 0.45f);
        _streams[(int)UiSound.Back] = MakeTone(new[] { 880f, 660f }, 120, Wave.Sine, 0.40f);
        _streams[(int)UiSound.Error] = MakeTone(new[] { 160f }, 160, Wave.Square, 0.22f);
        _streams[(int)UiSound.TabSwitch] = MakeTone(new[] { 900f }, 50, Wave.Sine, 0.30f);
        _streams[(int)UiSound.Cash] = MakeTone(new[] { 523.25f, 659.25f, 783.99f }, 180, Wave.Sine, 0.45f);
        _streams[(int)UiSound.Alert] = MakeTone(new[] { 988f, 1319f }, 220, Wave.Sine, 0.40f);
        _streams[(int)UiSound.DayTick] = MakeTone(new[] { 330f }, 90, Wave.Triangle, 0.25f);
    }

    /// <summary>
    /// Bakes a mono 16-bit clip: the duration split evenly across
    /// <paramref name="notes"/>, each with a ~5 ms linear attack and an
    /// exponential decay to near-zero so note boundaries and clip edges are
    /// click-free.
    /// </summary>
    private static AudioStreamWav MakeTone(float[] notes, int totalMs, Wave wave, float gain)
    {
        int totalSamples = MixRate * totalMs / 1000;
        int samplesPerNote = totalSamples / notes.Length;
        byte[] data = new byte[samplesPerNote * notes.Length * 2];
        int attackSamples = Math.Min(MixRate * 5 / 1000, samplesPerNote / 4);

        for (int n = 0; n < notes.Length; n++)
        {
            double phase = 0.0;
            double phaseStep = notes[n] / MixRate;
            for (int i = 0; i < samplesPerNote; i++)
            {
                double t = phase - Math.Floor(phase);
                double raw = wave switch
                {
                    Wave.Square => t < 0.5 ? 1.0 : -1.0,
                    Wave.Triangle => 4.0 * Math.Abs(t - 0.5) - 1.0,
                    _ => Math.Sin(Math.Tau * t),
                };
                double attack = attackSamples > 0 && i < attackSamples ? (double)i / attackSamples : 1.0;
                double decay = Math.Exp(-6.0 * i / samplesPerNote);
                short sample = (short)(raw * attack * decay * gain * short.MaxValue);

                int byteIndex = (n * samplesPerNote + i) * 2;
                data[byteIndex] = (byte)(sample & 0xFF);
                data[byteIndex + 1] = (byte)((sample >> 8) & 0xFF);
                phase += phaseStep;
            }
        }

        return new AudioStreamWav
        {
            Data = data,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = MixRate,
            Stereo = false,
        };
    }
}
