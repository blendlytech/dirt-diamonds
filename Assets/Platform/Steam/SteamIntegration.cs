using System.Reflection;
using System.Runtime.InteropServices;
using Godot;
using Steamworks;
using Steamworks.Data;

namespace DirtAndDiamonds.Platform.Steam;

/// <summary>
/// Sole owner of the Facepunch <see cref="SteamClient"/> lifecycle and the only
/// class in the codebase that calls the Steam SDK (steam_publishing_ship_it.md §2).
/// GameManager drives the three lifecycle points (_Ready → Initialize,
/// _Process → RunCallbacks, _ExitTree → Shutdown); everything else consumes the
/// thin wrappers below, every one of which no-ops when <see cref="IsAvailable"/>
/// is false. A missing Steam client (or a missing native redistributable) is the
/// normal development/headless state, not an error path — a failed init degrades
/// silently and must never block the boot.
/// </summary>
public sealed class SteamIntegration
{
    /// <summary>
    /// Spacewar, Valve's public development appid — swapped for the real id when
    /// the store page is minted (11e). Facepunch exports the appid into the
    /// process environment during Init, so dev runs against a live Steam client
    /// need no steam_appid.txt beside the executable.
    /// </summary>
    private const uint DevAppId = 480;

    // Resolver state is static because SetDllImportResolver binds per assembly
    // and throws on re-registration; a second SteamIntegration (there is only
    // ever one, but belt) must not re-register.
    private static bool _resolverRegistered;
    private static string[] _nativeProbeDirs = Array.Empty<string>();

    private bool _initialized;

    /// <summary>
    /// False whenever the SDK failed to come up (no Steam client running,
    /// native library missing, appid unknown). Every wrapper on this class
    /// no-ops on it — callers subscribe and call unconditionally, so the wiring
    /// is exercised identically with and without Steam (§2.2: no divergent
    /// code path to rot).
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>
    /// Brings the SDK up, or records that it isn't coming up this session.
    /// Called at the very top of GameManager._Ready — before the save opens
    /// (§5.4: Steam Auto-Cloud has already placed the freshest .db by the time
    /// this process runs, and IsAvailable must be known before any achievement
    /// subscriber wires).
    /// </summary>
    public void Initialize()
    {
        RegisterNativeResolver();
        try
        {
            // asyncCallbacks false: callbacks are pumped manually once per
            // frame from GameManager._Process, beside the bus pump — no Steam
            // background thread touching game state.
            SteamClient.Init(DevAppId, asyncCallbacks: false);
            _initialized = true;
            IsAvailable = true;
            GD.Print($"[Steam] up — appid {DevAppId}, user '{SteamClient.Name}'.");
        }
        catch (Exception e)
        {
            IsAvailable = false;
            GD.Print($"[Steam] not available — achievements/cloud disabled this session ({e.Message}).");
        }
    }

    /// <summary>One pump per frame (Facepunch requirement with async callbacks off).</summary>
    public void RunCallbacks()
    {
        if (!IsAvailable)
        {
            return;
        }
        SteamClient.RunCallbacks();
    }

    /// <summary>
    /// Steam-side unlock. Idempotent by design (§4.3): an already-unlocked
    /// achievement stays unlocked and re-asserting is a harmless no-op, which is
    /// why the game persists nothing to track what has fired. 11b's
    /// AchievementManager is the intended caller.
    /// </summary>
    public void TrySetAchievement(string achievementId)
    {
        if (!IsAvailable)
        {
            return;
        }
        new Achievement(achievementId).Trigger();
    }

    /// <summary>
    /// Steam-Stats counter increment (§4.3: counter achievements — Journeyman —
    /// hold their running total server-side, so the local save persists
    /// nothing). AddStat only stages the new value in the client; StoreStats
    /// pushes it and has Steam evaluate the stat-vs-threshold achievement rule.
    /// StoreStats is rate-limited on the order of minutes — fine for the
    /// once-per-season rollover cadence of the only caller.
    /// </summary>
    public void TryAddStat(string statId, int amount)
    {
        if (!IsAvailable)
        {
            return;
        }
        SteamUserStats.AddStat(statId, amount);
        SteamUserStats.StoreStats();
    }

    /// <summary>
    /// Friends-list status string (§6). Event-driven only — never called from a
    /// per-frame path (the ui_conventions no-per-frame-formatting rule
    /// generalizes to presence).
    /// </summary>
    public void SetRichPresence(string key, string value)
    {
        if (!IsAvailable)
        {
            return;
        }
        SteamFriends.SetRichPresence(key, value);
    }

    /// <summary>
    /// Called last in GameManager._ExitTree — after the database handle has
    /// released, so a Steam-triggered auto-cloud upload sees the finished file
    /// (§2.1). Idempotent; safe when init never succeeded.
    /// </summary>
    public void Shutdown()
    {
        if (!_initialized)
        {
            return;
        }
        _initialized = false;
        IsAvailable = false;
        SteamClient.Shutdown();
    }

    /// <summary>
    /// BUILD_PLAN §11: maps the Facepunch assembly's P/Invoke name (steam_api64
    /// on the Win64 build, libsteam_api on Posix — verified against the shipped
    /// binaries) to the redistributable sitting beside the managed assemblies or
    /// the executable, wherever this run put them. Registered once, before the
    /// first SteamClient call ever P/Invokes; returning Zero hands unmatched
    /// names back to the runtime's default probing.
    /// </summary>
    private static void RegisterNativeResolver()
    {
        if (_resolverRegistered)
        {
            return;
        }
        _resolverRegistered = true;

        var dirs = new List<string>(3);
        // Editor/dev builds: the natives are copied next to the game assembly
        // (.godot/mono/temp/bin/...) by the .csproj None items.
        string? assemblyDir = Path.GetDirectoryName(typeof(SteamClient).Assembly.Location);
        if (!string.IsNullOrEmpty(assemblyDir))
        {
            dirs.Add(assemblyDir);
        }
        dirs.Add(AppContext.BaseDirectory);
        // Exported builds: Steam convention puts the redistributable beside the
        // game executable.
        string? exeDir = Path.GetDirectoryName(OS.GetExecutablePath());
        if (!string.IsNullOrEmpty(exeDir))
        {
            dirs.Add(exeDir);
        }
        _nativeProbeDirs = dirs.ToArray();

        NativeLibrary.SetDllImportResolver(typeof(SteamClient).Assembly, ResolveSteamNative);
    }

    private static IntPtr ResolveSteamNative(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!libraryName.StartsWith("steam_api", StringComparison.OrdinalIgnoreCase)
            && !libraryName.StartsWith("libsteam_api", StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        string fileName = OperatingSystem.IsWindows() ? "steam_api64.dll" : "libsteam_api.so";
        foreach (string dir in _nativeProbeDirs)
        {
            string candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr handle))
            {
                return handle;
            }
        }
        return IntPtr.Zero;
    }
}
