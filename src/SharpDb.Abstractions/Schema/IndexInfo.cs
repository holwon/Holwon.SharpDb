namespace SharpDb.Abstractions.Schema;

/// <summary>
/// One index: the name a query planner would recognise, whether it enforces uniqueness, and its columns.
/// </summary>
/// <param name="Name">
/// The index's name.
/// </param>
/// <param name="IsUnique">
/// Whether the index admits only one row per key value.
/// </param>
/// <param name="Columns">
/// The indexed columns, in index order.
/// </param>
/// <remarks>
/// The index backing the primary key is deliberately absent from a <see cref="TableSchema"/>, which
/// reports the primary key as its own member; listing it in both places would present one fact as two.
/// </remarks>
public sealed record IndexInfo(string Name, bool IsUnique, IReadOnlyList<string> Columns);
