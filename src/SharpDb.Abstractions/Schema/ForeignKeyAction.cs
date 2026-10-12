namespace SharpDb.Abstractions.Schema;

/// <summary>
/// What the database does to a referencing row when the row it references is deleted or updated.
/// </summary>
/// <remarks>
/// These are the SQL standard's five actions, which every engine reports; the set is closed, so
/// the neutral model keeps it as an enum. The names serialize as written.
/// </remarks>
public enum ForeignKeyAction
{
    /// <summary>
    /// Lets the change through and leaves the reference dangling, for the application to police.
    /// </summary>
    NoAction,

    /// <summary>
    /// Refuses the change while a referencing row exists.
    /// </summary>
    Restrict,

    /// <summary>
    /// Applies the same change to the referencing rows.
    /// </summary>
    Cascade,

    /// <summary>
    /// Sets the referencing columns to null.
    /// </summary>
    SetNull,

    /// <summary>
    /// Sets the referencing columns to their declared default.
    /// </summary>
    SetDefault,
}
