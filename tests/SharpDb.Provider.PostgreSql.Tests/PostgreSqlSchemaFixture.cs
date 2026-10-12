using Npgsql;
using SharpDb.Provider.Contract;

namespace SharpDb.Provider.PostgreSql.Tests;

/// <summary>
/// A PostgreSQL database holding states the shared schema must keep out, each one written in the
/// engine's own SQL so a test that needs it can seed it.
/// </summary>
/// <remarks>
/// Everything here lives in the fixture's spare database rather than in the shared one, because
/// the shared suites assert the exact set of tables in the shared database and a test that seeded
/// its own would break that listing for whichever suite ran after it.
/// <para>
/// What lands here is what the shared suites cannot assert. Each item is a measured engine
/// behaviour rather than a preference:
/// <list type="bullet">
/// <item>
/// <description>
/// The <c>serial</c> spelling of auto-increment, which leaves no catalogue flag and is recognisable
/// only by the <c>nextval(…)</c> default the macro writes. The shared schema uses
/// <c>GENERATED … AS IDENTITY</c> and so exercises the flag instead.
/// </description>
/// </item>
/// <item>
/// <description>
/// A view over a base table whose columns are <c>NOT NULL</c> with defaults. Measured on
/// PostgreSQL 16.15, the view's columns report neither: PostgreSQL does not propagate a base
/// table's constraints into a view's catalogue entries, so a caller must not read a view's
/// <c>isNullable</c> as an answer about the column behind it.
/// </description>
/// </item>
/// </list>
/// </para>
/// </remarks>
/// <param name="database">The collection's fixture, which owns the container.</param>
internal sealed class PostgreSqlSchemaFixture(PostgreSqlContractDatabase database)
{
    /// <summary>
    /// The seeded fixture's spare database, where every state here is created.
    /// </summary>
    private readonly PostgreSqlContractDatabase _database = database;

    /// <summary>
    /// A connection string for the spare database, which carries no read-only mode of its own.
    /// </summary>
    /// <remarks>
    /// Seeding needs a writable connection: the Provider's own connections are read-only at the
    /// engine, so the statements here would be refused through one.
    /// </remarks>
    internal string ConnectionString => _database.SpareConnectionString;

    /// <summary>
    /// Creates the tables the engine-specific tests describe.
    /// </summary>
    /// <returns>A task that completes when the states exist.</returns>
    internal async Task CreateAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DROP VIEW IF EXISTS commented_view;
            DROP VIEW IF EXISTS serial_view;
            DROP TABLE IF EXISTS commented;
            DROP TABLE IF EXISTS indexed;
            DROP TABLE IF EXISTS serial_members;
            DROP TABLE IF EXISTS serial_users;
            DROP TABLE IF EXISTS serial_orgs;
            DROP TABLE IF EXISTS labelled;

            CREATE TABLE serial_orgs (
                id SERIAL,
                name VARCHAR(50) NOT NULL,
                PRIMARY KEY (id)
            );

            CREATE TABLE serial_users (
                id SERIAL,
                org_id INT NULL,
                email VARCHAR(50),
                PRIMARY KEY (id),
                CONSTRAINT fk_serial_users_org FOREIGN KEY (org_id) REFERENCES serial_orgs(id)
            );

            CREATE TABLE serial_members (
                id BIGSERIAL,
                label TEXT NOT NULL,
                PRIMARY KEY (id)
            );

            CREATE TABLE labelled (
                id SERIAL,
                required_not_null VARCHAR(50) NOT NULL DEFAULT 'a default',
                nullable_but_defaulted VARCHAR(50) DEFAULT 'another default',
                PRIMARY KEY (id)
            );

            CREATE VIEW serial_view AS
                SELECT id, required_not_null, nullable_but_defaulted FROM labelled;

            CREATE TABLE commented (
                id SERIAL,
                documented VARCHAR(50),
                undocumented VARCHAR(50),
                PRIMARY KEY (id)
            );
            COMMENT ON TABLE commented IS 'A table the catalogue describes';
            COMMENT ON COLUMN commented.documented IS 'A column with a comment';

            CREATE VIEW commented_view AS SELECT id, documented FROM commented;
            COMMENT ON VIEW commented_view IS 'A view the catalogue describes';

            CREATE TABLE indexed (
                id SERIAL,
                role VARCHAR(50) NOT NULL,
                org_id INT,
                PRIMARY KEY (id)
            );
            CREATE INDEX idx_indexed_role ON indexed(role);
            CREATE UNIQUE INDEX idx_indexed_role_unique ON indexed(role, org_id);
            CREATE INDEX idx_indexed_lower_role ON indexed(lower(role));
            CREATE INDEX idx_indexed_mixed ON indexed(lower(role), org_id);
            """;
        await command.ExecuteNonQueryAsync();
    }
}
