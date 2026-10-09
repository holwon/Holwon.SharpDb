namespace SharpDb.Abstractions.Schema;

/// <summary>
/// Whether a database object is a table or a view.
/// </summary>
/// <remarks>
/// The set is closed, so it is an enum. The declared data type is the open set — a type no one
/// has heard of arrives as a plain string — and that one stays a string for exactly that reason.
/// </remarks>
public enum TableType
{
    /// <summary>
    /// A base table, which stores its rows.
    /// </summary>
    Table,

    /// <summary>
    /// A view, whose rows are derived from a query rather than stored.
    /// </summary>
    View,
}
