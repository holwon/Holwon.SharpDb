using SharpDb.Abstractions;

namespace SharpDb.Provider.PostgreSql;

/// <summary>
/// What PostgreSQL contributes to the read-only Guard.
/// </summary>
/// <remarks>
/// Both halves are measured on PostgreSQL 16.15 rather than assumed, and each exists for a
/// different reason.
/// <para>
/// The dialect is what lets the Guard <em>understand</em> the engine's ordinary read-only SQL.
/// Measured, the generic dialect cannot parse <c>FILTER (WHERE …)</c>, which appears in perfectly
/// ordinary aggregates, so a PostgreSQL user writing one would be refused a statement that reads
/// and nothing else.
/// </para>
/// <para>
/// The denied functions are what keeps the Guard from being the <em>only</em> layer that matters.
/// A read-only session is not enough on this engine: measured, <c>pg_read_file</c> read the
/// server's own 30 KB configuration file and <c>pg_ls_dir</c> listed a directory, both inside a
/// session already set to read-only. These are the functions that reach the server host rather
/// than the database, and a read-only query has no business calling one.
/// </para>
/// </remarks>
internal static class PostgreSqlReadOnlyPolicy
{
    /// <summary>
    /// The policy the PostgreSQL Provider declares.
    /// </summary>
    /// <value>
    /// PostgreSQL's dialect and the five functions that read the server's own files.
    /// </value>
    /// <remarks>
    /// The list names only what is measured to be dangerous. <c>nextval</c> and <c>setval</c> are
    /// deliberately absent: they write to a sequence, which means the engine's own read-only mode
    /// refuses them already, so denying them here would add nothing the engine does not
    /// (ADR 0002, layer 2). These five are here precisely because that layer does not stop them.
    /// </remarks>
    internal static ReadOnlyPolicy Definition { get; } = new()
    {
        Dialect = SqlDialect.PostgreSql,
        DeniedFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pg_read_file",        // reads a file on the server host
            "pg_read_binary_file", // reads a file on the server host, as bytes
            "pg_ls_dir",           // lists a directory on the server host
            "pg_stat_file",        // reports one file's metadata on the server host
            "lo_import",           // reads a file on the server host into a large object
        },
    };
}
