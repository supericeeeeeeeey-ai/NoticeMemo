using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NoticeMemo.Core.Models;

namespace NoticeMemo.Core.Storage;

public sealed class SqliteMemoStore : IMemoStore
{
    private const int SqliteOk = 0;
    private const int SqliteRow = 100;
    private const int SqliteDone = 101;
    private const int OpenReadWrite = 0x00000002;
    private const int OpenCreate = 0x00000004;
    private const int OpenFullMutex = 0x00010000;
    private static readonly IntPtr SqliteTransient = new(-1);

    private readonly object _gate = new();
    private IntPtr _database;
    private bool _disposed;

    public SqliteMemoStore(string databasePath)
    {
        Location = databasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        var result = sqlite3_open_v2(
            Utf8Z(databasePath),
            out _database,
            OpenReadWrite | OpenCreate | OpenFullMutex,
            IntPtr.Zero);

        if (result != SqliteOk)
        {
            var message = _database == IntPtr.Zero ? "无法打开数据库" : ErrorMessage;
            if (_database != IntPtr.Zero)
            {
                sqlite3_close_v2(_database);
                _database = IntPtr.Zero;
            }

            throw new InvalidOperationException($"SQLite 初始化失败：{message}");
        }

        sqlite3_busy_timeout(_database, 5000);
        ExecuteScript("""
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            CREATE TABLE IF NOT EXISTS memos (
                id TEXT PRIMARY KEY NOT NULL,
                title TEXT NOT NULL,
                body TEXT NOT NULL,
                category TEXT NOT NULL,
                due_at TEXT NULL,
                location TEXT NULL,
                links TEXT NOT NULL,
                tags TEXT NOT NULL,
                priority INTEGER NOT NULL,
                status INTEGER NOT NULL,
                source_group TEXT NOT NULL,
                source_text TEXT NOT NULL,
                source_hash TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                completed_at TEXT NULL,
                reminder_sent_at TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_memos_status ON memos(status);
            CREATE INDEX IF NOT EXISTS idx_memos_due ON memos(due_at);
            CREATE INDEX IF NOT EXISTS idx_memos_hash ON memos(source_hash);
            """);

        EnsureColumn("reminder_sent_at", "TEXT NULL");
    }

    public string Location { get; }

    public string BackendName => "SQLite";

    public IReadOnlyList<Memo> GetAll()
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            using var statement = Prepare("""
                SELECT id, title, body, category, due_at, location, links, tags,
                       priority, status, source_group, source_text, source_hash,
                       created_at, updated_at, completed_at, reminder_sent_at
                FROM memos
                ORDER BY CASE WHEN status = 2 THEN 1 ELSE 0 END,
                         CASE WHEN due_at IS NULL THEN 1 ELSE 0 END,
                         due_at, created_at DESC;
                """);

            var memos = new List<Memo>();
            while (Step(statement) == SqliteRow)
            {
                memos.Add(MapMemo(statement));
            }

            return memos;
        }
    }

    public Memo? Get(string id)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            using var statement = Prepare("""
                SELECT id, title, body, category, due_at, location, links, tags,
                       priority, status, source_group, source_text, source_hash,
                       created_at, updated_at, completed_at, reminder_sent_at
                FROM memos WHERE id = $id LIMIT 1;
                """);
            Bind(statement, 1, id);
            return Step(statement) == SqliteRow ? MapMemo(statement) : null;
        }
    }

    public void Upsert(Memo memo)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            using var statement = Prepare("""
                INSERT OR REPLACE INTO memos (
                    id, title, body, category, due_at, location, links, tags,
                    priority, status, source_group, source_text, source_hash,
                    created_at, updated_at, completed_at, reminder_sent_at
                ) VALUES (
                    $id, $title, $body, $category, $due, $location, $links, $tags,
                    $priority, $status, $source_group, $source_text, $source_hash,
                    $created, $updated, $completed, $reminder_sent
                );
                """);

            Bind(statement, 1, memo.Id);
            Bind(statement, 2, memo.Title);
            Bind(statement, 3, memo.Body);
            Bind(statement, 4, memo.Category);
            Bind(statement, 5, memo.DueAt?.ToString("O", CultureInfo.InvariantCulture));
            Bind(statement, 6, memo.Location);
            Bind(statement, 7, JsonSerializer.Serialize(memo.Links));
            Bind(statement, 8, JsonSerializer.Serialize(memo.Tags));
            Bind(statement, 9, (long)memo.Priority);
            Bind(statement, 10, (long)memo.Status);
            Bind(statement, 11, memo.SourceGroup);
            Bind(statement, 12, memo.SourceText);
            Bind(statement, 13, memo.SourceHash);
            Bind(statement, 14, memo.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            Bind(statement, 15, memo.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
            Bind(statement, 16, memo.CompletedAt?.ToString("O", CultureInfo.InvariantCulture));
            Bind(statement, 17, memo.ReminderSentAt?.ToString("O", CultureInfo.InvariantCulture));
            StepToDone(statement);
        }
    }

    public void Delete(string id)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            using var statement = Prepare("DELETE FROM memos WHERE id = $id;");
            Bind(statement, 1, id);
            StepToDone(statement);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_database != IntPtr.Zero)
        {
            sqlite3_close_v2(_database);
            _database = IntPtr.Zero;
        }
    }

    private static Memo MapMemo(SqliteStatement statement)
    {
        return new Memo
        {
            Id = ReadString(statement, 0),
            Title = ReadString(statement, 1),
            Body = ReadString(statement, 2),
            Category = ReadString(statement, 3),
            DueAt = ReadDate(statement, 4),
            Location = ReadNullableString(statement, 5),
            Links = DeserializeList(ReadString(statement, 6)),
            Tags = DeserializeList(ReadString(statement, 7)),
            Priority = (MemoPriority)ReadInt64(statement, 8),
            Status = (MemoStatus)ReadInt64(statement, 9),
            SourceGroup = ReadString(statement, 10),
            SourceText = ReadString(statement, 11),
            SourceHash = ReadString(statement, 12),
            CreatedAt = ReadDate(statement, 13) ?? DateTimeOffset.Now,
            UpdatedAt = ReadDate(statement, 14) ?? DateTimeOffset.Now,
            CompletedAt = ReadDate(statement, 15),
            ReminderSentAt = ReadDate(statement, 16)
        };
    }

    private void EnsureColumn(string columnName, string declaration)
    {
        using var statement = Prepare("PRAGMA table_info(memos);");
        while (Step(statement) == SqliteRow)
        {
            if (string.Equals(ReadString(statement, 1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        ExecuteScript($"ALTER TABLE memos ADD COLUMN {columnName} {declaration};");
    }

    private static List<string> DeserializeList(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string ReadString(SqliteStatement statement, int column)
    {
        var pointer = sqlite3_column_text(statement.Handle, column);
        if (pointer == IntPtr.Zero)
        {
            return "";
        }

        var length = sqlite3_column_bytes(statement.Handle, column);
        var bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string? ReadNullableString(SqliteStatement statement, int column)
    {
        return sqlite3_column_type(statement.Handle, column) == 5 ? null : ReadString(statement, column);
    }

    private static long ReadInt64(SqliteStatement statement, int column) => sqlite3_column_int64(statement.Handle, column);

    private static DateTimeOffset? ReadDate(SqliteStatement statement, int column)
    {
        var value = ReadNullableString(statement, column);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static void Bind(SqliteStatement statement, int index, string? value)
    {
        if (value is null)
        {
            sqlite3_bind_null(statement.Handle, index);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        var result = sqlite3_bind_text(statement.Handle, index, bytes, bytes.Length, SqliteTransient);
        if (result != SqliteOk)
        {
            throw new InvalidOperationException($"SQLite 绑定文本失败：{result}");
        }
    }

    private static void Bind(SqliteStatement statement, int index, long value)
    {
        var result = sqlite3_bind_int64(statement.Handle, index, value);
        if (result != SqliteOk)
        {
            throw new InvalidOperationException($"SQLite 绑定整数失败：{result}");
        }
    }

    private SqliteStatement Prepare(string sql)
    {
        var bytes = Utf8Z(sql);
        var result = sqlite3_prepare_v2(_database, bytes, -1, out var handle, IntPtr.Zero);
        if (result != SqliteOk)
        {
            throw new InvalidOperationException($"SQLite 准备语句失败：{ErrorMessage}");
        }

        return new SqliteStatement(handle);
    }

    private int Step(SqliteStatement statement)
    {
        var result = sqlite3_step(statement.Handle);
        if (result is SqliteRow or SqliteDone)
        {
            return result;
        }

        throw new InvalidOperationException($"SQLite 执行失败：{ErrorMessage}");
    }

    private void StepToDone(SqliteStatement statement)
    {
        if (Step(statement) != SqliteDone)
        {
            throw new InvalidOperationException("SQLite 语句没有正常完成。");
        }
    }

    private void ExecuteScript(string sql)
    {
        var result = sqlite3_exec(_database, Utf8Z(sql), IntPtr.Zero, IntPtr.Zero, out var errorPointer);
        if (result == SqliteOk)
        {
            return;
        }

        var message = errorPointer == IntPtr.Zero ? ErrorMessage : Marshal.PtrToStringUTF8(errorPointer) ?? ErrorMessage;
        if (errorPointer != IntPtr.Zero)
        {
            sqlite3_free(errorPointer);
        }

        throw new InvalidOperationException($"SQLite 初始化语句失败：{message}");
    }

    private string ErrorMessage => Marshal.PtrToStringUTF8(sqlite3_errmsg(_database)) ?? "未知 SQLite 错误";

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SqliteMemoStore));
        }
    }

    private static byte[] Utf8Z(string value) => Encoding.UTF8.GetBytes(value + "\0");

    private sealed class SqliteStatement : IDisposable
    {
        public SqliteStatement(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                sqlite3_finalize(Handle);
            }
        }
    }

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2(byte[] filename, out IntPtr database, int flags, IntPtr vfs);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close_v2(IntPtr database);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(IntPtr database, byte[] sql, int numBytes, out IntPtr statement, IntPtr tail);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr statement);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr statement);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_bytes(IntPtr statement, int column);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern long sqlite3_column_int64(IntPtr statement, int column);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_type(IntPtr statement, int column);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_text(IntPtr statement, int index, byte[] value, int length, IntPtr destructor);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_null(IntPtr statement, int index);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_busy_timeout(IntPtr database, int milliseconds);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_errmsg(IntPtr database);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr database, byte[] sql, IntPtr callback, IntPtr callbackArgument, out IntPtr errorMessage);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void sqlite3_free(IntPtr pointer);
}
