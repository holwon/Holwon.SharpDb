using SharpDb.Abstractions;
using SharpDb.Provider.MsSql;

namespace SharpDb.Provider.MsSql.Tests;

/// <summary>
/// What the SQL Server Provider declares, asserted without a container: the parts of its contract
/// that are pure data and pure shape.
/// </summary>
/// <remarks>
/// The behaviour is asserted against a real server in this project's other tests. What is here is
/// what can be answered without one — and it is worth answering here rather than only there,
/// because these are the facts the running server reads at startup, before any database is
/// reached.
/// </remarks>
public class MsSqlDatabaseProviderTests
{
    private readonly IDatabaseProvider _provider = new MsSqlDatabaseProvider();

    /// <summary>
    /// The identifier is the one the configuration selects by.
    /// </summary>
    /// <remarks>
    /// <c>mssql</c> is a published contract rather than an internal name: a user writes it into
    /// their <c>mcp.json</c>, so changing it would break every existing configuration. Asserted
    /// literally, because that is what the promise is.
    /// </remarks>
    [Fact]
    public void The_type_is_the_published_identifier()
    {
        Assert.Equal("mssql", _provider.Type);
    }

    /// <summary>
    /// The policy names the dialect this engine's SQL is written in.
    /// </summary>
    [Fact]
    public void The_policy_names_sql_servers_own_dialect()
    {
        Assert.Equal(SqlDialect.MsSql, _provider.Policy.Dialect);
    }

    /// <summary>
    /// The policy denies the four rowset functions that reach outside the database.
    /// </summary>
    /// <remarks>
    /// The names are asserted rather than only their count, because the point of the list is
    /// <em>which</em> functions are on it. The count is asserted too, so a name added without
    /// reason is noticed as well as one removed.
    /// </remarks>
    [Fact]
    public void The_policy_denies_the_rowset_functions()
    {
        var denied = _provider.Policy.DeniedFunctions;

        Assert.Equal(4, denied.Count);
        Assert.Contains("OPENROWSET", denied);
        Assert.Contains("OPENQUERY", denied);
        Assert.Contains("OPENDATASOURCE", denied);
        Assert.Contains("OPENXML", denied);
    }

    /// <summary>
    /// A denied name is matched without regard to case, whichever way it is written.
    /// </summary>
    /// <remarks>
    /// The Guard compares names case-insensitively, so the policy's own comparison has to agree or
    /// a statement spelling <c>openquery</c> would slip past a list holding <c>OPENQUERY</c>.
    /// </remarks>
    [Theory]
    [InlineData("openquery")]
    [InlineData("OpenQuery")]
    [InlineData("OPENQUERY")]
    public void A_denied_name_is_matched_without_regard_to_case(string written)
    {
        Assert.Contains(written, _provider.Policy.DeniedFunctions);
    }

    /// <summary>
    /// The policy's net does not reach past what it denies.
    /// </summary>
    /// <remarks>
    /// The list is a net inside the permit list, not a policy of its own: a Provider that denied
    /// ordinary functions would be turning away read-only queries. <c>count</c> is the control.
    /// </remarks>
    [Fact]
    public void The_policy_denies_nothing_ordinary()
    {
        Assert.DoesNotContain("count", _provider.Policy.DeniedFunctions);
        Assert.DoesNotContain("getdate", _provider.Policy.DeniedFunctions);
        Assert.DoesNotContain("newid", _provider.Policy.DeniedFunctions);
    }

    /// <summary>
    /// Every member that rejects a missing argument says so rather than failing later.
    /// </summary>
    /// <remarks>
    /// Asserted for this Provider because it is a contract every Provider keeps, and a new one is
    /// where it is likeliest to be forgotten.
    /// </remarks>
    [Fact]
    public async Task A_missing_connection_is_rejected_rather_than_failing_later()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _provider.ListTablesAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _provider.ListDatabasesAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _provider.DescribeTableAsync(null!, "users", CancellationToken.None));
    }

    /// <summary>
    /// A missing table name is rejected rather than being sent to the server.
    /// </summary>
    /// <remarks>
    /// The connection is one that never opens, and that is what makes the assertion possible
    /// without a server: the argument check runs before anything reaches the database, so a
    /// connection pointing at nowhere is never used. A Provider that passed the null along would
    /// fault on the server round trip instead, and this would catch it.
    /// </remarks>
    [Fact]
    public async Task A_missing_table_name_is_rejected_before_the_server_is_reached()
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection("Server=nowhere;Database=nowhere");

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _provider.DescribeTableAsync(connection, null!, CancellationToken.None));
    }
}
