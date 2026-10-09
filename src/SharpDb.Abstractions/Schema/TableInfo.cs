namespace SharpDb.Abstractions.Schema;

/// <summary>
/// One table or view as a listing reports it: enough to identify the object, and nothing more.
/// </summary>
/// <param name="Name">
/// The object's name, as the database spells it.
/// </param>
/// <param name="Type">
/// Whether the object is a table or a view.
/// </param>
/// <param name="Comment">
/// The database's own description of the object, or <see langword="null"/> when it keeps none.
/// </param>
/// <remarks>
/// A read model rather than part of the aggregate: it has no child records and carries no
/// invariant, so it is safe to hand out on its own.
/// </remarks>
public sealed record TableInfo(string Name, TableType Type, string? Comment);
