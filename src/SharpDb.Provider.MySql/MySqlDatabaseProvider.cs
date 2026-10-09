using System.Data.Common;
using MySqlConnector;
using SharpDb.Abstractions;

namespace SharpDb.Provider.MySql;

/// <summary>
/// Serves MySQL databases, reaching them exclusively through the Driver this project owns.
/// </summary>
public sealed class MySqlDatabaseProvider : IDatabaseProvider
{
    /// <summary>
    /// Puts the session in read-only mode; every write it would carry is refused by the server.
    /// </summary>
    private const string EnterReadOnlySession = "SET SESSION TRANSACTION READ ONLY";

    /// <inheritdoc/>
    public string Type => "mysql";

    /// <inheritdoc/>
    /// <remarks>
    /// Read-only cannot ride along in the connection string here, so the session is switched to
    /// read-only immediately after connecting and before the connection is handed out — the mode
    /// is in force before a single statement runs (ADR 0002, layer 2).
    /// </remarks>
    public async Task<DbConnection> CreateReadOnlyConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);

        var connection = new MySqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = EnterReadOnlySession;
            await command.ExecuteNonQueryAsync(cancellationToken);

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
