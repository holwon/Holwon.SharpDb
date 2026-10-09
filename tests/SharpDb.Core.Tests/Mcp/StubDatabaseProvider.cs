using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using SharpDb.Abstractions;
using SharpDb.Abstractions.Schema;

namespace SharpDb.Core.Tests.Mcp;

/// <summary>
/// A Provider that answers from fixed data, so the MCP surface can be exercised without a database.
/// </summary>
/// <remarks>
/// This is a test double, not a second implementation: it exists so the MCP seam can be tested for
/// the thing it is responsible for — the tools the client sees, and the shape of what they answer —
/// while the real introspection stays covered by the Provider's own tests, where a real database
/// is available. It records how often it was asked, so a test can tell whether a call reached the
/// Provider at all.
/// </remarks>
internal sealed class StubDatabaseProvider : IDatabaseProvider
{
    /// <summary>
    /// The tables every listing reports, in the order the Provider returns them.
    /// </summary>
    internal static readonly IReadOnlyList<TableInfo> Tables =
    [
        new("users", TableType.Table, "User accounts"),
        new("active_users", TableType.View, null),
    ];

    /// <summary>
    /// The databases every listing reports.
    /// </summary>
    internal static readonly IReadOnlyList<string> Databases = ["main"];

    /// <inheritdoc/>
    public string Type => "stub";

    /// <inheritdoc/>
    public Task<DbConnection> CreateReadOnlyConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        // Returned closed and never opened: the double answers no statement, so nothing in the MCP
        // seam has any use for an open handle, and the tool disposes it either way.
        return Task.FromResult<DbConnection>(new StubDbConnection());
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TableInfo>> ListTablesAsync(
        DbConnection connection,
        CancellationToken cancellationToken) => Task.FromResult(Tables);

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ListDatabasesAsync(
        DbConnection connection,
        CancellationToken cancellationToken) => Task.FromResult(Databases);

    /// <summary>
    /// A connection with no backing store, since the double never runs a statement on it.
    /// </summary>
    private sealed class StubDbConnection : DbConnection
    {
        /// <inheritdoc/>
        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        /// <inheritdoc/>
        public override string Database => string.Empty;

        /// <inheritdoc/>
        public override string DataSource => string.Empty;

        /// <inheritdoc/>
        public override string ServerVersion => string.Empty;

        /// <inheritdoc/>
        public override ConnectionState State { get; }

        /// <inheritdoc/>
        public override void ChangeDatabase(string databaseName) { }

        /// <inheritdoc/>
        public override void Close() { }

        /// <inheritdoc/>
        public override void Open() { }

        /// <inheritdoc/>
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException("The double runs no transactions.");

        /// <inheritdoc/>
        protected override DbCommand CreateDbCommand() =>
            throw new NotSupportedException("The double runs no statements.");
    }
}
