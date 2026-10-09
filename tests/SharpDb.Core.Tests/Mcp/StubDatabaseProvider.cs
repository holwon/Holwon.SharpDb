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

    /// <summary>
    /// The structure reported for the one table the double can describe.
    /// </summary>
    /// <remarks>
    /// It carries one of everything the model can hold — a comment, an auto-increment column, a
    /// default, a foreign key with two different actions, and an index — so the shape the Tool
    /// publishes is exercised whole rather than one field at a time. It is also deliberately
    /// coherent: the foreign key's local column is a column of the table, as it would have to be in
    /// a real database.
    /// </remarks>
    internal static readonly TableSchema Users = new(
        Name: "users",
        Type: TableType.Table,
        Comment: "User accounts",
        Columns:
        [
            new ColumnInfo("id", 1, "INTEGER", IsNullable: false, DefaultValue: null, IsAutoIncrement: true, Comment: null),
            new ColumnInfo("org_id", 2, "INTEGER", IsNullable: false, DefaultValue: null, IsAutoIncrement: false, Comment: null),
            new ColumnInfo("name", 3, "TEXT", IsNullable: false, DefaultValue: null, IsAutoIncrement: false, Comment: "Full name"),
            new ColumnInfo("role", 4, "TEXT", IsNullable: false, DefaultValue: "'member'", IsAutoIncrement: false, Comment: null),
        ],
        PrimaryKey: ["id"],
        ForeignKeys:
        [
            new ForeignKeyInfo("fk_users_org", ["org_id"], "orgs", ["id"], ForeignKeyAction.Cascade, ForeignKeyAction.NoAction),
        ],
        Indexes:
        [
            new IndexInfo("idx_users_name", IsUnique: false, Columns: ["name"]),
        ]);

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
    public Task<TableSchema?> DescribeTableAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken) => Task.FromResult(
            string.Equals(tableName, Users.Name, StringComparison.Ordinal) ? Users : null);

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
