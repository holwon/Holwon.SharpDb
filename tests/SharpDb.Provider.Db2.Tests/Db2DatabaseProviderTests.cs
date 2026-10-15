using SharpDb.Abstractions;
using SharpDb.Provider.Db2;

namespace SharpDb.Provider.Db2.Tests;

/// <summary>
/// What the Db2 Provider declares, asserted without a container: the parts of its contract that are
/// pure data and pure shape.
/// </summary>
/// <remarks>
/// The behaviour is asserted against a real server in this project's other tests. What is here is
/// what can be answered without one — and it is worth answering here rather than only there,
/// because these are the facts the running server reads at startup, before any database is
/// reached.
/// </remarks>
public class Db2DatabaseProviderTests
{
    private readonly IDatabaseProvider _provider = new Db2DatabaseProvider();

    /// <summary>
    /// The identifier is the one the configuration selects by.
    /// </summary>
    /// <remarks>
    /// <c>db2</c> is a published contract rather than an internal name: a user writes it into
    /// their <c>mcp.json</c>, so changing it would break every existing configuration. Asserted
    /// literally, because that is what the promise is.
    /// </remarks>
    [Fact]
    public void The_type_is_the_published_identifier()
    {
        Assert.Equal("db2", _provider.Type);
    }

    /// <summary>
    /// The policy declares Db2's dialect.
    /// </summary>
    [Fact]
    public void The_policy_declares_the_db2_dialect()
    {
        Assert.Equal(SqlDialect.Db2, _provider.Policy.Dialect);
    }

    /// <summary>
    /// The denied function set is empty, and that is a measurement rather than an oversight.
    /// </summary>
    [Fact]
    public void The_denied_functions_are_empty()
    {
        Assert.Empty(_provider.Policy.DeniedFunctions);
    }
}
