namespace DirtAndDiamonds.Data;

/// <summary>Why boot refused to open the save (<see cref="SaveCompatibility.Inspect"/>).</summary>
public enum SaveRefusal : byte
{
    /// <summary>Loadable: a v14 save, or no save at all (missing, 0-byte or never-schema'd → a new world).</summary>
    None = 0,

    /// <summary>A save from before the v14 clean break (user_version 1–13). The player may start over.</summary>
    OlderSave = 1,

    /// <summary>A save from a newer build (user_version above 14). Never loaded and never renamed.</summary>
    NewerSave = 2,
}

/// <summary>
/// The schema v14 clean break (league_restructure.md §14, 13-1 brief §3.2.5).
/// The game hasn't released, so an older save is refused, never migrated, and
/// never deleted. Boot reads the save's <c>PRAGMA user_version</c> before the
/// schema script runs (<see cref="Inspect"/>, which writes nothing). Only when
/// the player confirms does <see cref="ConfirmCleanBreak"/> fold the WAL back
/// in and rename the file aside, after which boot starts again on an empty
/// path. A save from a newer build is only ever refused. From 1.0 on, every
/// schema change ships a migration instead (GEMINI.md §4).
///
/// Engine-independent (no Godot types), so SchemaValidator drives the exact
/// code the game runs.
/// </summary>
public static class SaveCompatibility
{
    public const int CurrentSchemaVersion = 14;

    /// <summary>
    /// The backup's version tag. The spec names the backup
    /// <c>dirt_and_diamonds.v13.bak</c> (§14): it marks "the pre-v14 save",
    /// whichever pre-v14 version it actually is.
    /// </summary>
    public const string BackupTag = "v13";

    /// <summary>
    /// Decides whether the save at <paramref name="dbPath"/> can load. Reads
    /// only (a read-only, unpooled peek), so the file is untouched whatever the
    /// answer. A missing file, a 0-byte file and a SQLite file at
    /// user_version 0 are all "no save yet": a new world, no refusal.
    /// </summary>
    public static SaveRefusal Inspect(string dbPath, out int schemaVersion)
    {
        schemaVersion = 0;
        if (!File.Exists(dbPath) || new FileInfo(dbPath).Length == 0)
        {
            return SaveRefusal.None;
        }

        schemaVersion = DatabaseManager.PeekSchemaVersion(dbPath);
        if (schemaVersion > CurrentSchemaVersion)
        {
            return SaveRefusal.NewerSave;
        }
        return schemaVersion is > 0 and < CurrentSchemaVersion ? SaveRefusal.OlderSave : SaveRefusal.None;
    }

    /// <summary>
    /// The player said "Start a new career": checkpoints the old save's WAL
    /// into the .db (<see cref="DatabaseManager.CheckpointForSync"/>), closes
    /// it, and moves the .db plus any remaining <c>-wal</c>/<c>-shm</c>
    /// sidecars aside (<see cref="MoveAside"/>). Returns the backup's path.
    /// Re-inspects first and throws unless the save really is an older one, so
    /// a newer or current save can never be renamed through this path.
    /// </summary>
    public static string ConfirmCleanBreak(string dbPath)
    {
        SaveRefusal refusal = Inspect(dbPath, out int version);
        if (refusal != SaveRefusal.OlderSave)
        {
            throw new InvalidOperationException(
                $"Refusing to move '{dbPath}' aside: it is not a pre-v{CurrentSchemaVersion} save " +
                $"(user_version {version}, {refusal}).");
        }

        // Fold the WAL in so the backup's .db is self-contained. A lingering
        // reader can stop the checkpoint from completing; the sidecars then
        // travel with the backup below, so nothing is lost either way.
        using (var old = new DatabaseManager(dbPath))
        {
            old.CheckpointForSync();
        }
        return MoveAside(dbPath);
    }

    /// <summary>
    /// Renames <paramref name="dbPath"/> to <c>{name}.v13.bak</c> (then
    /// <c>.v13.1.bak</c>, <c>.v13.2.bak</c>, …: the first slot whose file and
    /// sidecars are all free), moves any <c>-wal</c>/<c>-shm</c> sidecars to the
    /// same name, and returns the backup path. Never overwrites: the moves pass
    /// no overwrite flag, so a race throws instead of clobbering. The main file
    /// moves first; if a sidecar move then fails this throws before boot opens
    /// a new save, so a stale WAL can never be replayed into the fresh world.
    /// </summary>
    public static string MoveAside(string dbPath)
    {
        string stem = Path.Combine(
            Path.GetDirectoryName(dbPath) ?? string.Empty, Path.GetFileNameWithoutExtension(dbPath));
        for (int n = 0; ; n++)
        {
            string backup = n == 0 ? $"{stem}.{BackupTag}.bak" : $"{stem}.{BackupTag}.{n}.bak";
            if (File.Exists(backup) || File.Exists(backup + "-wal") || File.Exists(backup + "-shm"))
            {
                continue;
            }
            File.Move(dbPath, backup);
            foreach (string sidecar in new[] { "-wal", "-shm" })
            {
                if (File.Exists(dbPath + sidecar))
                {
                    File.Move(dbPath + sidecar, backup + sidecar);
                }
            }
            return backup;
        }
    }
}
