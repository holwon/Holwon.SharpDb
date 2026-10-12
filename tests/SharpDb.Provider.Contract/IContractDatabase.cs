using SharpDb.Abstractions;

namespace SharpDb.Provider.Contract;

/// <summary>
/// What the shared contract suites need from one engine: the Provider to exercise, a database it
/// is bound to, and whether that engine can be reached at all.
/// </summary>
/// <remarks>
/// Each engine's test project supplies an implementation of its own — SQLite through a temporary
/// file, MySQL and PostgreSQL through containers — so the same assertions run against every engine
/// and the port is verified as one contract rather than three implementations that merely resemble
/// one another.
/// </remarks>
public interface IContractDatabase
{
    /// <summary>
    /// The identifier the engine is known by, such as <c>mysql</c>, <c>postgresql</c> or <c>sqlite</c>.
    /// </summary>
    /// <value>
    /// The value the Provider's own <see cref="IDatabaseProvider.Type"/> must equal.
    /// </value>
    string Type { get; }

    /// <summary>
    /// The Provider under contract.
    /// </summary>
    /// <value>
    /// The engine's own implementation, reached only through the interface.
    /// </value>
    IDatabaseProvider Provider { get; }

    /// <summary>
    /// A connection string that reaches the seeded database, carrying no read-only mode of its own.
    /// </summary>
    /// <value>
    /// The string handed to the Provider, which is what adds the engine's read-only mode.
    /// </value>
    string ConnectionString { get; }

    /// <summary>
    /// The name of the database the connection is bound to.
    /// </summary>
    /// <value>
    /// The name a database listing must report among the visible ones.
    /// </value>
    string DatabaseName { get; }

    /// <summary>
    /// A statement that outlives the one-second timeout the timeout test allows.
    /// </summary>
    /// <value>
    /// One read-only statement, written in the engine's own SQL, that takes longer than a second.
    /// </value>
    /// <remarks>
    /// The statement must be one the Guard permits, because the test sends it through the Tool.
    /// It differs per engine — there is no way to make one statement slow on both — which is why
    /// it is part of what the fixture supplies rather than of the shared suite.
    /// </remarks>
    string SlowQuery { get; }

    /// <summary>
    /// Whether the engine can be reached at all.
    /// </summary>
    /// <value>
    /// <see langword="true"/> when the database is seeded and ready; <see langword="false"/> when
    /// the engine could not be started, in which case every contract test skips.
    /// </value>
    /// <remarks>
    /// Only the absence of the engine's own precondition may yield <see langword="false"/> — for
    /// MySQL, a machine without a working Docker. A failure of the engine itself once it is
    /// reachable is a defect and must propagate rather than skip.
    /// </remarks>
    bool Available { get; }

    /// <summary>
    /// Why the engine is unavailable, in words a reader of the test output can act on.
    /// </summary>
    /// <value>
    /// The skip reason, meaningful only when <see cref="Available"/> is <see langword="false"/>.
    /// </value>
    string SkipReason { get; }

    /// <summary>
    /// A value an error message must never carry: the engine's secret, or a path of this machine.
    /// </summary>
    /// <value>
    /// What ADR 0010 protects — for MySQL the password the server was given, for SQLite the
    /// directory the database file lives in. The shared suite asserts that a failed call does not
    /// repeat it.
    /// </value>
    /// <remarks>
    /// One value per engine, supplied by the fixture because only the fixture holds it. It is
    /// deliberately one value rather than a set: the assertion is a tripwire for a message that
    /// starts disclosing what it should not, not a scan for everything the fixture happens to
    /// know. Neither engine's messages carry it today — measured — so the assertion stands as a
    /// regression guard rather than as a reproduction.
    /// </remarks>
    string SensitiveText { get; }
}
