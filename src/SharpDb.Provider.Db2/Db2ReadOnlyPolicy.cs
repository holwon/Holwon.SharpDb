using SharpDb.Abstractions;

namespace SharpDb.Provider.Db2;

/// <summary>
/// What Db2 contributes to the read-only Guard.
/// </summary>
/// <remarks>
/// The dialect is Db2's own value, and that value is what selects the placeholder rewrite: Db2
/// binds by position (<c>?</c>), so a caller writes <c>@name</c> and Core replaces every
/// placeholder with <c>?</c> before the statement reaches this Driver.
/// <para>
/// The denied list is empty, and that is a measurement rather than an oversight. The list exists
/// for functions that work inside the engine's own read-only mode but reach outside the query —
/// measured on PostgreSQL 16.15, <c>pg_read_file</c> and <c>pg_ls_dir</c> do exactly that. No such
/// function was measured on Db2: what writes there is refused by the account's privileges (the
/// only boundary on this engine, since Db2 has no engine-level read-only mode at all), and what
/// reads server state reads it through catalogue views a <c>SELECT</c>-only account may already
/// see. If one is found, it is added here, because the list may only grow (ADR 0013).
/// </para>
/// </remarks>
internal static class Db2ReadOnlyPolicy
{
    /// <summary>
    /// The policy the Db2 Provider declares.
    /// </summary>
    /// <value>
    /// Db2's dialect and no denied function names.
    /// </value>
    internal static ReadOnlyPolicy Definition { get; } = new()
    {
        Dialect = SqlDialect.Db2,
    };
}
