namespace SharpDb.Abstractions.Schema;

/// <summary>
/// One table's whole structure: its columns, primary key, foreign keys and indexes.
/// </summary>
/// <param name="Name">
/// The table's or view's name, as the database spells it.
/// </param>
/// <param name="Type">
/// Whether the object is a table or a view.
/// </param>
/// <param name="Comment">
/// The database's own description of the object, or <see langword="null"/> when it keeps none.
/// </param>
/// <param name="Columns">
/// Every column, in declaration order.
/// </param>
/// <param name="PrimaryKey">
/// The primary key's columns, in key order.
/// </param>
/// <param name="ForeignKeys">
/// Every foreign key declared on the table.
/// </param>
/// <param name="Indexes">
/// Every index except the one backing the primary key.
/// </param>
/// <remarks>
/// This is the Schema model's single aggregate root. Its child records — <see cref="ColumnInfo"/>,
/// <see cref="ForeignKeyInfo"/> and <see cref="IndexInfo"/> — have no meaning apart from the table
/// that owns them, and it is where the model's invariants sit: <paramref name="Indexes"/> never
/// repeats the primary key, and <paramref name="PrimaryKey"/> is a sequence rather than a set,
/// because a composite key's order is part of the key. The invariants hold because whoever builds
/// a <see cref="TableSchema"/> satisfies them, which is what makes it the aggregate's root rather
/// than a bundle of lists.
/// </remarks>
public sealed record TableSchema(
    string Name,
    TableType Type,
    string? Comment,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<string> PrimaryKey,
    IReadOnlyList<ForeignKeyInfo> ForeignKeys,
    IReadOnlyList<IndexInfo> Indexes);
