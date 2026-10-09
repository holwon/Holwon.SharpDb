namespace SharpDb.Abstractions.Schema;

/// <summary>
/// One column of a table: its identity, its declaration, and what a caller needs to write valid SQL.
/// </summary>
/// <param name="Name">
/// The column's name.
/// </param>
/// <param name="Ordinal">
/// The column's 1-based position in the table's declaration order.
/// </param>
/// <param name="DataType">
/// The type exactly as the database declared it.
/// </param>
/// <param name="IsNullable">
/// Whether the column accepts null.
/// </param>
/// <param name="DefaultValue">
/// The column's default, as the expression text the database stores, or <see langword="null"/>
/// when it has none.
/// </param>
/// <param name="IsAutoIncrement">
/// Whether the database generates the column's value on insert.
/// </param>
/// <param name="Comment">
/// The database's own description of the column, or <see langword="null"/> when it keeps none.
/// </param>
/// <remarks>
/// <paramref name="DataType"/> stays the raw declaration — <c>bigint(20) unsigned</c>,
/// <c>VARCHAR(255)</c>, or the empty string a SQLite column may carry — because predicting how the
/// database will treat a value is the whole point of asking. Normalising it into a small set of
/// names would throw that away.
/// <para>
/// <paramref name="IsAutoIncrement"/> is as often computed as it is read: SQLite records no such
/// flag, so its Provider derives it from an <c>INTEGER</c> primary key.
/// </para>
/// </remarks>
public sealed record ColumnInfo(
    string Name,
    int Ordinal,
    string DataType,
    bool IsNullable,
    string? DefaultValue,
    bool IsAutoIncrement,
    string? Comment);
