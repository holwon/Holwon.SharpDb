using SharpDb.Provider.Contract;

namespace SharpDb.Provider.MsSql.Tests;

/// <summary>
/// The provider contract suite, run against a real SQL Server: what a listing reports, what a
/// description reports, and that the connection arrives open.
/// </summary>
/// <remarks>
/// The other half of the contract — that a write is refused by the engine — is not here because it
/// is not part of the port: SQL Server has no read-only mode of its own to refuse one with
/// (ADR 0016). The refusal this engine really has is the account's, and it is asserted in this
/// project's own tests.
/// </remarks>
/// <param name="database">The seeded database the collection shares.</param>
[Collection(MsSqlContractCollection.Name)]
public sealed class MsSqlProviderContractTests(MsSqlContractDatabase database)
    : ProviderContractSuite(database);
