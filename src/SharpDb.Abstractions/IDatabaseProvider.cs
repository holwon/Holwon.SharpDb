using System.Data.Common;
using SharpDb.Abstractions.Schema;

namespace SharpDb.Abstractions;

/// <summary>
/// The single seam a database type is implemented behind: connection creation, introspection and
/// dialect all sit behind this one interface, because they never vary independently (ADR 0003).
/// </summary>
/// <remarks>
/// The interface grows to its documented five members as the tickets that need them land. Each
/// member arrives with its first caller rather than in advance, because a member declared before
/// anything calls it has to be stubbed in every implementation — and a stub is a promise the
/// interface cannot keep.
/// </remarks>
public interface IDatabaseProvider
{
    /// <summary>
    /// The identifier matched against <c>SHARPDB__TYPE</c>, such as <c>mysql</c> or <c>sqlite</c>.
    /// </summary>
    /// <value>
    /// The canonical lower-case name of the database type this provider serves.
    /// </value>
    string Type { get; }

    /// <summary>
    /// Opens a connection that is already in the engine's own read-only mode.
    /// </summary>
    /// <param name="connectionString">The connection string to reach the database with.</param>
    /// <param name="cancellationToken">Cancels the attempt to open the connection.</param>
    /// <returns>
    /// An open connection on which every statement the engine would treat as a write is refused
    /// by the engine itself. The caller owns it and must dispose it.
    /// </returns>
    /// <exception cref="DbException">The database could not be reached.</exception>
    Task<DbConnection> CreateReadOnlyConnectionAsync(string connectionString, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the tables and views the connection can see, ordered by name.
    /// </summary>
    /// <param name="connection">An open connection to the database to inspect.</param>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>
    /// One entry per table and view, ordered by name.
    /// </returns>
    /// <exception cref="DbException">The database refused the query.</exception>
    /// <remarks>
    /// Only the connection's own database is inspected; the server is bound to one.
    /// Views are listed alongside tables, and each entry says which it is.
    /// </remarks>
    Task<IReadOnlyList<TableInfo>> ListTablesAsync(DbConnection connection, CancellationToken cancellationToken);

    /// <summary>
    /// Describes one table or view: its columns, primary key, foreign keys and indexes.
    /// </summary>
    /// <param name="connection">An open connection to the database to inspect.</param>
    /// <param name="tableName">The name of the table or view to describe.</param>
    /// <param name="cancellationToken">Cancels the description.</param>
    /// <returns>
    /// The table's whole structure, or <see langword="null"/> when the database holds no table or
    /// view by that name.
    /// </returns>
    /// <exception cref="DbException">The database refused the query.</exception>
    /// <remarks>
    /// A missing table is reported as <see langword="null"/> rather than as an exception, because
    /// not finding a name a caller guessed is an answer, not a fault. Deciding what to tell the
    /// client about it belongs to the Tool, which is where the wording lives.
    /// <para>
    /// Only the connection's own database is inspected; the server is bound to one. Whether a name
    /// that differs in case from the stored one matches is left to the engine, which is what
    /// defines identifier matching: SQLite folds case either way, while a server that does not may
    /// need the name spelled as the catalogue holds it.
    /// </para>
    /// </remarks>
    Task<TableSchema?> DescribeTableAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the databases the connection can see, for information only.
    /// </summary>
    /// <param name="connection">An open connection to the database to ask.</param>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>
    /// The names of the visible databases, ordered by name.
    /// </returns>
    /// <exception cref="DbException">The database refused the query.</exception>
    /// <remarks>
    /// Nothing else can be done with the other names: every other member works on the connection's
    /// own database, so this reports what exists rather than what is reachable.
    /// </remarks>
    Task<IReadOnlyList<string>> ListDatabasesAsync(DbConnection connection, CancellationToken cancellationToken);
}
