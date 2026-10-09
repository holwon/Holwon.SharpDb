using System.Data.Common;

namespace SharpDb.Abstractions;

/// <summary>
/// The single seam a database type is implemented behind: connection creation, introspection and
/// dialect all sit behind this one interface, because they never vary independently (ADR 0003).
/// </summary>
/// <remarks>
/// The interface grows to its documented five members as the tickets that need them land;
/// startup probing needs the first two, and nothing else is declared before it has a caller.
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
}
