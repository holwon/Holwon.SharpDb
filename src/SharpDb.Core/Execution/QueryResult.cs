namespace SharpDb.Core.Execution;

/// <summary>
/// One result set: the columns it has, the rows it returned, and whether a row limit cut it short.
/// </summary>
/// <remarks>
/// The columns are named once and every row holds its values in that same order, so a reader is
/// told each column name a single time rather than once per row.
/// <para>
/// This is the value object the query Tool answers with; it carries no identity and is never
/// mutated. A row is a plain list rather than a map, for the reason above.
/// </para>
/// </remarks>
/// <param name="Columns">The column names, in the order the rows' values follow.</param>
/// <param name="Rows">The rows, each a list of values in column order.</param>
/// <param name="Truncated">Whether more rows were available than the limit allowed.</param>
internal sealed record QueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    bool Truncated);
