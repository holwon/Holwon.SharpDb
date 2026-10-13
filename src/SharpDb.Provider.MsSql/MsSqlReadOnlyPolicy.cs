using SharpDb.Abstractions;

namespace SharpDb.Provider.MsSql;

/// <summary>
/// What SQL Server contributes to the read-only Guard.
/// </summary>
/// <remarks>
/// Both halves are measured on SQL Server 2022 rather than assumed, and each exists for a
/// different reason.
/// <para>
/// The dialect is what lets the Guard <em>understand</em> the engine's ordinary read-only SQL.
/// Measured, the generic dialect refuses <c>[bracketed]</c> identifiers outright — the parser
/// reports <c>Expected identifier, found [</c> — while the SQL Server dialect reads them and
/// <c>TOP n</c> as well. Brackets are how this engine's users write a name that is a keyword or
/// contains a space, so a Guard that could not read them would be turning away the engine's own
/// ordinary SQL.
/// </para>
/// <para>
/// The denied functions are what keeps a query from reaching outside the database. They matter more
/// here than on any other engine, and for a reason that is the whole subject of ADR 0016: SQL
/// Server has <em>no engine-level read-only mode</em>, so there is no layer beneath this one that
/// would refuse a query the Guard let through. The four names below are the rowset functions that
/// read a file on the server or a table on another server, and they are the only thing standing
/// between a permitted <c>SELECT</c> and the server's disk or its neighbours.
/// </para>
/// </remarks>
internal static class MsSqlReadOnlyPolicy
{
    /// <summary>
    /// The policy the SQL Server Provider declares.
    /// </summary>
    /// <value>
    /// The SQL Server dialect and the four rowset functions that reach outside the database.
    /// </value>
    /// <remarks>
    /// The list names only what has no read-only meaning, and the two failure modes it guards
    /// against are worth telling apart.
    /// <para>
    /// <c>xp_cmdshell</c> and <c>sp_executesql</c> are deliberately absent: they are reached
    /// through <c>EXEC</c>, which the permit list does not name at all, so they are refused as a
    /// statement kind before any function table could matter.
    /// </para>
    /// <para>
    /// A version of this list naming four functions is therefore complete for what it does — a
    /// function only needs denying when a statement containing it parses as a <c>SELECT</c>.
    /// </para>
    /// </remarks>
    internal static ReadOnlyPolicy Definition { get; } = new()
    {
        Dialect = SqlDialect.MsSql,
        DeniedFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "OPENROWSET",     // reads a file on the server, or a table on another server
            "OPENQUERY",      // runs a query against a linked server
            "OPENDATASOURCE", // reaches a data source named in the statement itself
            "OPENXML",        // reads a document handed to it as a value
        },
    };
}
