namespace SharpDb.Abstractions;

/// <summary>
/// The statement forms a Provider's read-only policy is written over.
/// </summary>
/// <remarks>
/// A dialect is named as a value rather than handed over as a parser object, so that the
/// Abstractions project declares <em>that</em> a Provider has a dialect without depending on the
/// parser (ADR 0005 as amended). The mapping from a value to a parser dialect lives in Core, which
/// is the only place a parser is referenced at all.
/// </remarks>
public enum SqlDialect
{
    /// <summary>
    /// The parser's dialect that is not tied to one engine.
    /// </summary>
    /// <value>
    /// What the MySQL and SQLite Providers declare: it accepts double-quoted and back-quoted
    /// identifiers, which is the superset those engines' SQL is written in today.
    /// </value>
    Generic,

    /// <summary>
    /// PostgreSQL's own dialect.
    /// </summary>
    /// <value>
    /// What the PostgreSQL Provider declares. Measured: it reads <c>FILTER (WHERE …)</c>, which
    /// the generic dialect cannot parse, and it does <em>not</em> accept back-quoted identifiers.
    /// </value>
    PostgreSql,

    /// <summary>
    /// Oracle's own dialect.
    /// </summary>
    /// <value>
    /// What the Oracle Provider declares. Read with the generic dialect, because the parser this
    /// project pins has no Oracle dialect and the generic one parses Oracle's ordinary read-only
    /// SQL. The value is named for the engine all the same, because it is what selects the one
    /// engine-specific rewrite there is: Oracle spells a placeholder <c>:name</c> where every
    /// other engine in use spells it <c>@name</c>.
    /// </value>
    Oracle,

    /// <summary>
    /// SQL Server's own dialect.
    /// </summary>
    /// <value>
    /// What the SQL Server Provider declares. Measured, it is the only dialect of the ones this
    /// project names that accepts <c>[bracketed]</c> identifiers — the generic dialect refuses them
    /// outright — and it reads <c>TOP n</c> as well. So the one accepted gap this project carried
    /// for SQLite (<c>SELECT * FROM [my table]</c> being turned away) is closed for the engine
    /// whose SQL is actually written that way.
    /// </value>
    MsSql,
}
