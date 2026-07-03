using Microsoft.Data.Sqlite;

namespace DirtAndDiamonds.Data;

/// <summary>
/// Sole owner of the SQLite connection for the entire game. No other class may
/// open a connection, hold a connection string, or construct raw SQL — the
/// simulation systems consume typed query classes (e.g. <see cref="PlayerQueries"/>)
/// which acquire pooled, prepared commands from this manager.
///
/// Deliberately engine-independent (no Godot types) so headless tools — the
/// validate_sqlite_schema harness, future Monte Carlo batch runners — drive the
/// exact code path the game uses.
///
/// Threading: one connection, one writer. Life-sim and baseball-sim writes
/// arrive independently through the async event bus (Phase 2) and commit in
/// separate batches; they must never share a transaction. The internal lock
/// guards command-pool and batch bookkeeping, but readers returned by
/// <see cref="ExecuteReader"/> must be drained on the calling thread before
/// another system touches the database.
/// </summary>
public sealed class DatabaseManager : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Dictionary<string, SqliteCommand> _commandPool = new();
    private readonly object _dbLock = new();
    private SqliteTransaction? _activeBatch;
    private bool _disposed;

    public string DatabasePath { get; }

    /// <summary>True while a batch transaction (calendar tick) is open.</summary>
    public bool IsBatchActive
    {
        get { lock (_dbLock) { return _activeBatch is not null; } }
    }

    public DatabaseManager(string databasePath)
    {
        DatabasePath = databasePath;

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Single long-lived connection for the session; disabling ADO pooling
            // guarantees the file handle is released the moment we dispose, so
            // save-file management (copy/backup/cloud sync) never fights a lock.
            Pooling = false,
            // PRAGMA foreign_keys=ON on open — enforced on every connection.
            ForeignKeys = true,
        };

        _connection = new SqliteConnection(builder.ConnectionString);
        _connection.Open();
        ConfigureConnection();
    }

    /// <summary>
    /// Connection-scoped pragmas that cannot live in SchemaDefinitions.sql.
    /// WAL lets the Gritty Event dispatcher poll while a sim batch writes;
    /// synchronous=NORMAL is the recommended WAL pairing (durable enough for a
    /// game save, roughly 2x faster batch commits); busy_timeout covers the
    /// polling reader briefly colliding with a checkpoint.
    /// </summary>
    private void ConfigureConnection()
    {
        string? journalMode = ExecuteConstantScalar("PRAGMA journal_mode = WAL;") as string;
        if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Failed to enable WAL journal mode on '{DatabasePath}' (got '{journalMode}').");
        }

        ExecuteConstantScalar("PRAGMA synchronous = NORMAL;");
        ExecuteConstantScalar("PRAGMA busy_timeout = 5000;");

        long foreignKeys = (long)(ExecuteConstantScalar("PRAGMA foreign_keys;") ?? 0L);
        if (foreignKeys != 1)
        {
            throw new InvalidOperationException(
                $"foreign_keys pragma is not enabled on '{DatabasePath}'.");
        }
    }

    // ------------------------------------------------------------------
    // Schema lifecycle
    // ------------------------------------------------------------------

    /// <summary>
    /// Applies the versioned DDL script (SchemaDefinitions.sql). The script is
    /// idempotent (CREATE ... IF NOT EXISTS) and is the only non-parameterized
    /// SQL path in the codebase — it must only ever receive the checked-in
    /// schema file, never constructed SQL.
    /// </summary>
    public void InitializeSchema(string schemaScriptPath) =>
        ApplySchema(File.ReadAllText(schemaScriptPath));

    /// <summary>
    /// Text overload for callers whose schema file isn't on the filesystem —
    /// in exported builds res:// lives inside the .pck, so GameManager reads
    /// the DDL through Godot's FileAccess and hands the text here. Same
    /// contract: checked-in SchemaDefinitions.sql content only.
    /// </summary>
    public void ApplySchema(string ddl)
    {
        lock (_dbLock)
        {
            ThrowIfDisposed();
            if (_activeBatch is not null)
            {
                throw new InvalidOperationException("Cannot apply schema while a batch transaction is active.");
            }

            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = ddl;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Current PRAGMA user_version — the save's schema version for migrations.</summary>
    public int GetSchemaVersion() =>
        Convert.ToInt32(ExecuteConstantScalar("PRAGMA user_version;") ?? 0);

    /// <summary>
    /// Journal mode, FK enforcement and schema version in one call, for boot-time
    /// assertions and the validation harness.
    /// </summary>
    public (string JournalMode, bool ForeignKeysEnabled, int SchemaVersion) GetConnectionDiagnostics()
    {
        string journalMode = (string)(ExecuteConstantScalar("PRAGMA journal_mode;") ?? "unknown");
        bool foreignKeys = (long)(ExecuteConstantScalar("PRAGMA foreign_keys;") ?? 0L) == 1;
        return (journalMode, foreignKeys, GetSchemaVersion());
    }

    /// <summary>Runs PRAGMA integrity_check; returns "ok" when the file is sound.</summary>
    public string RunIntegrityCheck() =>
        (string)(ExecuteConstantScalar("PRAGMA integrity_check;") ?? "no result");

    /// <summary>Counts orphaned rows reported by PRAGMA foreign_key_check (0 = healthy).</summary>
    public int RunForeignKeyCheck()
    {
        lock (_dbLock)
        {
            ThrowIfDisposed();
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_key_check;";
            command.Transaction = _activeBatch;
            using SqliteDataReader reader = command.ExecuteReader();
            int violations = 0;
            while (reader.Read())
            {
                violations++;
            }
            return violations;
        }
    }

    // ------------------------------------------------------------------
    // Pooled command acquisition & execution
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns the session-cached command for <paramref name="sql"/>, creating it
    /// on first request. Query classes acquire their commands once (in their
    /// constructor), attach typed parameters, call <see cref="SqliteCommand.Prepare"/>,
    /// and then only mutate parameter values per call — no per-call allocation
    /// inside simulation loops. <paramref name="sql"/> must be a compile-time
    /// constant; interpolated SQL is a review-blocking defect.
    /// </summary>
    public SqliteCommand GetPooledCommand(string sql)
    {
        lock (_dbLock)
        {
            ThrowIfDisposed();
            if (!_commandPool.TryGetValue(sql, out SqliteCommand? command))
            {
                command = _connection.CreateCommand();
                command.CommandText = sql;
                _commandPool.Add(sql, command);
            }
            return command;
        }
    }

    /// <summary>Executes a pooled command, enlisting it in the active batch if one is open.</summary>
    public int ExecuteNonQuery(SqliteCommand command)
    {
        lock (_dbLock)
        {
            ThrowIfDisposed();
            command.Transaction = _activeBatch;
            return command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Executes a pooled query command. The reader must be disposed before any
    /// other database call is made from another system (single-connection rule).
    /// </summary>
    public SqliteDataReader ExecuteReader(SqliteCommand command)
    {
        lock (_dbLock)
        {
            ThrowIfDisposed();
            command.Transaction = _activeBatch;
            return command.ExecuteReader();
        }
    }

    public object? ExecuteScalar(SqliteCommand command)
    {
        lock (_dbLock)
        {
            ThrowIfDisposed();
            command.Transaction = _activeBatch;
            return command.ExecuteScalar();
        }
    }

    // ------------------------------------------------------------------
    // Batch transactions (one per calendar tick — never one per row)
    // ------------------------------------------------------------------

    /// <summary>
    /// Opens the single batch transaction for a calendar tick. Nested batches are
    /// a design error: Life-sim and Baseball-sim writes commit separately and must
    /// never share a transaction.
    /// </summary>
    public void BeginBatch()
    {
        lock (_dbLock)
        {
            ThrowIfDisposed();
            if (_activeBatch is not null)
            {
                throw new InvalidOperationException(
                    "A batch transaction is already active. One transaction per calendar tick; " +
                    "Life and Baseball writes must not share a batch.");
            }
            _activeBatch = _connection.BeginTransaction();
        }
    }

    public void CommitBatch()
    {
        lock (_dbLock)
        {
            ThrowIfDisposed();
            if (_activeBatch is null)
            {
                throw new InvalidOperationException("CommitBatch called with no active batch.");
            }
            _activeBatch.Commit();
            _activeBatch.Dispose();
            _activeBatch = null;
        }
    }

    public void RollbackBatch()
    {
        lock (_dbLock)
        {
            ThrowIfDisposed();
            if (_activeBatch is null)
            {
                return;
            }
            _activeBatch.Rollback();
            _activeBatch.Dispose();
            _activeBatch = null;
        }
    }

    /// <summary>Runs <paramref name="work"/> inside a single batch transaction, rolling back on failure.</summary>
    public void RunInBatch(Action work)
    {
        BeginBatch();
        try
        {
            work();
            CommitBatch();
        }
        catch
        {
            RollbackBatch();
            throw;
        }
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    /// <summary>
    /// Scalar execution reserved for compile-time-constant pragma statements.
    /// Never routed through user or simulation data.
    /// </summary>
    private object? ExecuteConstantScalar(string constantSql)
    {
        lock (_dbLock)
        {
            ThrowIfDisposed();
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = constantSql;
            command.Transaction = _activeBatch;
            return command.ExecuteScalar();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DatabaseManager));
        }
    }

    public void Dispose()
    {
        lock (_dbLock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            _activeBatch?.Rollback();
            _activeBatch?.Dispose();
            _activeBatch = null;

            foreach (SqliteCommand command in _commandPool.Values)
            {
                command.Dispose();
            }
            _commandPool.Clear();

            _connection.Dispose();
        }
    }
}
