namespace DirtAndDiamonds.Data;

/// <summary>What <see cref="SaveCompatibility.Prepare"/> found at the save path.</summary>
public enum SaveCheckOutcome
{
    /// <summary>No save (missing, 0-byte, or an empty file with no schema): a new world will be generated.</summary>
    NewSave,
    /// <summary>A save at the current schema version: load it.</summary>
    Current,
    /// <summary>An older save was moved aside (never deleted); a new world will be generated.</summary>
    LegacyBackedUp,
}

/// <summary>
/// The v14 clean break (league_restructure.md §14, GEMINI.md §4): before the
/// schema script runs, boot checks the save's <c>PRAGMA user_version</c>. A save
/// older than <see cref="CurrentSchemaVersion"/> can't be loaded, so it is
/// renamed out of the way (never deleted) and the caller generates a new world.
/// From 1.0 on, every schema change ships a migration instead.
/// </summary>
public static class SaveCompatibility
{
    public const int CurrentSchemaVersion = 14;

    /// <summary>
    /// Inspects the save at <paramref name="dbPath"/> without modifying it
    /// (read-only, unpooled connection), and moves a legacy save aside.
    /// Throws on a save from a NEWER build: loading it could corrupt it.
    /// </summary>
    public static SaveCheckOutcome Prepare(string dbPath, out string? backupPath)
    {
        backupPath = null;
        if (!File.Exists(dbPath) || new FileInfo(dbPath).Length == 0)
        {
            return SaveCheckOutcome.NewSave;
        }

        int version = DatabaseManager.PeekSchemaVersion(dbPath);
        if (version == 0)
        {
            return SaveCheckOutcome.NewSave; // a SQLite file that never received the schema
        }
        if (version > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"The save at '{dbPath}' is schema v{version}, newer than this build (v{CurrentSchemaVersion}). " +
                "It was left untouched.");
        }
        if (version == CurrentSchemaVersion)
        {
            return SaveCheckOutcome.Current;
        }

        backupPath = BackupLegacySave(dbPath, version);
        return SaveCheckOutcome.LegacyBackedUp;
    }

    /// <summary>
    /// Moves a legacy save (and its <c>-wal</c>/<c>-shm</c> sidecars, if any)
    /// out of the save path and returns the backup's path. The spec names the
    /// backup <c>dirt_and_diamonds.v13.bak</c> (§14); the hard rule is that a
    /// player's save is never deleted or overwritten.
    /// </summary>
    /// <remarks>
    /// Naming: <c>name.v13.bak</c>, then <c>name.v13.1.bak</c>, <c>.2</c>, …
    /// The first free slot wins, so an older backup is never overwritten (a
    /// downgrade-then-upgrade keeps both). The main file moves first; its WAL
    /// sidecars follow under the backup's name, so the backup stays openable
    /// with its un-checkpointed pages. If a sidecar move fails, this throws
    /// before boot opens a new save, so a stale WAL can never be replayed
    /// into the fresh world.
    /// </remarks>
    private static string BackupLegacySave(string dbPath, int version)
    {
        string stem = Path.Combine(Path.GetDirectoryName(dbPath) ?? "", Path.GetFileNameWithoutExtension(dbPath));
        for (int n = 0; ; n++)
        {
            string backup = n == 0 ? $"{stem}.v{version}.bak" : $"{stem}.v{version}.{n}.bak";
            if (File.Exists(backup) || File.Exists(backup + "-wal") || File.Exists(backup + "-shm"))
            {
                continue;
            }
            File.Move(dbPath, backup); // no overwrite flag: throws rather than clobber
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
