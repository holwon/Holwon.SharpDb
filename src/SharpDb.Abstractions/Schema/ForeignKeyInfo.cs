namespace SharpDb.Abstractions.Schema;

/// <summary>
/// One foreign key: which local columns point at which table, and what follows a delete or update.
/// </summary>
/// <param name="Name">
/// The constraint's name, or the database's own identifier for it when the schema declared none.
/// </param>
/// <param name="Columns">
/// The local columns, in the order the constraint declares them.
/// </param>
/// <param name="ReferencedTable">
/// The table the local columns point at.
/// </param>
/// <param name="ReferencedColumns">
/// The referenced columns, positionally matched to <paramref name="Columns"/>.
/// </param>
/// <param name="OnDelete">
/// What the database does when the referenced row is deleted.
/// </param>
/// <param name="OnUpdate">
/// What the database does when the referenced key is updated.
/// </param>
/// <remarks>
/// <paramref name="ReferencedColumns"/> may be empty, and that is not an error: a foreign key that
/// points at the parent's primary key without naming its columns has nothing to report.
/// </remarks>
public sealed record ForeignKeyInfo(
    string Name,
    IReadOnlyList<string> Columns,
    string ReferencedTable,
    IReadOnlyList<string> ReferencedColumns,
    ForeignKeyAction OnDelete,
    ForeignKeyAction OnUpdate);
