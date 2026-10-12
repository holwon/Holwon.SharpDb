namespace SharpDb.Abstractions;

/// <summary>
/// What one engine contributes to the read-only Guard: the dialect its statements are read with,
/// and the functions a read-only query must not call on it.
/// </summary>
/// <remarks>
/// This is data, not behaviour. The Guard's <em>algorithm</em> is Core's and is the same for every
/// database type — measured across the two dialects, 43 statements classify identically (ADR 0005)
/// — while the two things that genuinely differ per engine are these. Handing them over as values
/// is what lets a new database type arrive without Core changing, and what keeps the permit list
/// one policy rather than one per implementation: a policy may only <em>add</em> denied function
/// names, never widen what is allowed.
/// </remarks>
public sealed record ReadOnlyPolicy
{
    /// <summary>
    /// The dialect the engine's statements are read with.
    /// </summary>
    /// <value>
    /// The dialect, named as a value rather than as a parser object.
    /// </value>
    public required SqlDialect Dialect { get; init; }

    /// <summary>
    /// The functions a read-only query must not call on this engine.
    /// </summary>
    /// <value>
    /// Function names, compared without regard to case. Names the Guard already refuses are
    /// harmless to repeat; the set is a net that catches escape hatches written as ordinary calls.
    /// </value>
    /// <remarks>
    /// These are functions that read or affect state outside the query — a server's files, an
    /// advisory lock, the server's own time — and so have no read-only meaning even though the
    /// parser is right to call the statement containing them a <c>SELECT</c>. The engine layer
    /// still stands behind this, but it is not sufficient on its own: measured on PostgreSQL
    /// 16.15, <c>pg_read_file</c> and <c>pg_ls_dir</c> work perfectly well in a read-only session.
    /// </remarks>
    public IReadOnlySet<string> DeniedFunctions { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
