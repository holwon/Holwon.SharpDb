using SharpDb.Abstractions;

namespace SharpDb.Provider.Oracle;

/// <summary>
/// What Oracle contributes to the read-only Guard.
/// </summary>
/// <remarks>
/// The dialect is Oracle's own value, and that value is what selects the placeholder rewrite: Oracle
/// spells a placeholder <c>:name</c> where every other engine in use spells it <c>@name</c>, so a
/// caller writes <c>@name</c> and Core respells it before the statement reaches this Driver.
/// <para>
/// The denied list is empty, and that is a measurement rather than an oversight. The list exists for
/// functions that work inside the engine's own read-only mode but reach outside the query —
/// measured on PostgreSQL 16.15, <c>pg_read_file</c> and <c>pg_ls_dir</c> do exactly that. No such
/// function was measured on Oracle: what writes there is refused by the read-only transaction
/// itself, and what reads server state reads it through dictionary views a read-only account may
/// already see. If one is found, it is added here, because the list may only grow (ADR 0013).
/// </para>
/// </remarks>
internal static class OracleReadOnlyPolicy
{
    /// <summary>
    /// The policy the Oracle Provider declares.
    /// </summary>
    /// <value>
    /// Oracle's dialect and no denied function names.
    /// </value>
    internal static ReadOnlyPolicy Definition { get; } = new()
    {
        Dialect = SqlDialect.Oracle,
    };
}
