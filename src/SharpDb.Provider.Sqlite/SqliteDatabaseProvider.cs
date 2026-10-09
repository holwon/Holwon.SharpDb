using System.Data.Common;
using Microsoft.Data.Sqlite;
using SharpDb.Abstractions;

namespace SharpDb.Provider.Sqlite;

/// <summary>
/// Serves SQLite databases, reaching one exclusively through the SQLite Driver this project owns.
/// </summary>
public sealed class SqliteDatabaseProvider : IDatabaseProvider
{
    /// <inheritdoc/>
    public string Type => "sqlite";

    /// <inheritdoc/>
    /// <remarks>
    /// Read-only is expressed in the connection string itself, so the engine opens the file
    /// read-only and the mode is in force before a single statement runs (ADR 0002, layer 2).
    /// </remarks>
    public async Task<DbConnection> CreateReadOnlyConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString)
        {
            Mode = SqliteOpenMode.ReadOnly,
        };

        var connection = new SqliteConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
