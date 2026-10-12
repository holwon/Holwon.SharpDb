using SharpDb.Abstractions;

namespace SharpDb.Core.Execution;

/// <summary>
/// Respells a statement's placeholders into the form the engine that will run it expects.
/// </summary>
/// <remarks>
/// A caller writes <c>@name</c> whichever database answers, and only the engine whose Driver wants
/// another spelling is given one. Measured, Oracle is that engine: <c>SELECT @id FROM dual</c> is a
/// syntax error there (<c>ORA-00936</c>), and only <c>:id</c> binds — whichever way
/// <c>BindByName</c> is set, because the marker is part of the grammar rather than of the binding.
/// <para>
/// The rewrite belongs to Core rather than to the Provider because the Provider is not on the
/// execution path: it never sees the caller's statement, and the command the statement runs on is
/// Core's. What the Provider does contribute is the <see cref="SqlDialect"/> value, which is what
/// selects the rewrite — so the decision is the engine's declaration and the mechanism is Core's.
/// </para>
/// </remarks>
public static class PlaceholderTranslation
{
    /// <summary>
    /// The marker Oracle's Driver expects in a statement's text.
    /// </summary>
    private const char OracleMarker = ':';

    /// <summary>
    /// Respells a statement's placeholders for the engine whose dialect was declared.
    /// </summary>
    /// <param name="sql">The statement, exactly as the caller wrote it.</param>
    /// <param name="dialect">The dialect the selected Provider's policy declares.</param>
    /// <returns>
    /// The statement to hand to the Driver. Every engine but Oracle writes <c>@name</c>, so for
    /// them the statement is returned unchanged.
    /// </returns>
    /// <exception cref="ArgumentNullException">The statement is <see langword="null"/>.</exception>
    /// <remarks>
    /// The rewrite reuses the placeholder scan, so an <c>@</c> inside a string literal, a quoted
    /// identifier or a comment is left alone: it is data or prose, not a placeholder. Only the
    /// marker changes; the names, and every other character of the statement, are as the caller
    /// wrote them.
    /// </remarks>
    public static string ForDialect(string sql, SqlDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(sql);

        return dialect == SqlDialect.Oracle
            ? ParameterPlaceholders.WithMarker(sql, OracleMarker)
            : sql;
    }
}
