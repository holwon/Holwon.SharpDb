using Microsoft.Data.Sqlite;

namespace SharpDb.Provider.Sqlite.Tests;

/// <summary>
/// A real SQLite database in a directory the test owns: seeded through a writable connection,
/// then handed to the Provider to serve read-only.
/// </summary>
/// <remarks>
/// The database is a file rather than a shared-cache in-memory database, and that is a deliberate,
/// measured choice. A shared-cache in-memory database requires <c>Mode=Memory</c>, while the
/// Provider must open every connection in <c>Mode=ReadOnly</c> (ADR 0002, layer 2) — and
/// <see cref="SqliteOpenMode"/> admits one mode at a time. Measured on Microsoft.Data.Sqlite
/// 10.0.12: the Provider's read-only connection to a <c>Mode=Memory;Cache=Shared</c> database
/// fails outright with <c>SQLite Error 14: unable to open database file</c>; the URI spelling
/// <c>file:name?mode=memory&amp;cache=shared</c> does open, but the URI's own <c>mode</c> then wins
/// and writes succeed, so it would pass while proving nothing. A file is the only substrate on
/// which the Provider's connection string, its read-only guarantee, and these assertions are all
/// true at once.
/// <para>
/// Note what the ticket that asked for an in-memory database assumed: that SQLite ignores
/// <c>SQLITE_OPEN_READONLY</c> for an in-memory database. It does not — measured here,
/// <c>Data Source=:memory:;Mode=ReadOnly</c> refuses writes with <c>attempt to write a readonly
/// database</c>. The obstacle is narrower than assumed, and specific to the named shared-cache form.
/// </para>
/// <para>
/// <c>Pooling=False</c> is what lets the file be deleted afterwards: a pooled connection would keep
/// the handle open past disposal, and Windows refuses to delete a file that is still in use.
/// </para>
/// </remarks>
internal sealed class SqliteTestDatabase : IDisposable
{
    private readonly string _directory;

    private SqliteTestDatabase(string directory, string connectionString)
    {
        _directory = directory;
        ConnectionString = connectionString;
    }

    /// <summary>
    /// The connection string a caller would supply, with no read-only mode of its own.
    /// </summary>
    /// <value>
    /// The string the Provider is handed; the Provider is what adds the read-only mode.
    /// </value>
    internal string ConnectionString { get; }

    /// <summary>
    /// Creates a database, runs the given schema against it through a writable connection, and
    /// closes that connection so the Provider is the only one that ever opens the file.
    /// </summary>
    /// <param name="schemaSql">The DDL to seed the database with, which may hold several statements.</param>
    /// <returns>The seeded database.</returns>
    internal static async Task<SqliteTestDatabase> CreateAsync(string schemaSql)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sharpdb-sqlite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var connectionString = $"Data Source={Path.Combine(directory, "sharpdb.sqlite")};Pooling=False";

        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = schemaSql;
            await command.ExecuteNonQueryAsync();
        }

        return new SqliteTestDatabase(directory, connectionString);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // The Provider owns none of these connections across a call, so by now no handle is open.
        Directory.Delete(_directory, recursive: true);
    }
}
