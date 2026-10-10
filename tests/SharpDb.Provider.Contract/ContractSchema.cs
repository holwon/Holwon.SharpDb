namespace SharpDb.Provider.Contract;

/// <summary>
/// The one database both engines seed, written in each engine's own SQL, so the shared suites can
/// assert the same answers against either Provider.
/// </summary>
/// <remarks>
/// The two scripts describe the same structure — and the differences between them are the whole
/// point of this file, because every one of them was forced by a measured engine behaviour or a
/// measured divergence between the two.
/// <para>
/// The rule the shape follows: <em>what the shared suites assert must be answerable identically by
/// both engines.</em> Where an engine cannot express something — or expresses it in a way that
/// leaks into the Schema model — the shared schema leaves it out, and the engine-specific tests
/// cover it in the engine's own project. Recorded here so the omissions are not mistaken for
/// oversights:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <c>org_id</c> is nullable. MySQL refuses <c>ON UPDATE SET NULL</c> on a <c>NOT NULL</c> column
/// (<c>Column 'org_id' cannot be NOT NULL: needed in a foreign key constraint … SET NULL</c>), so a
/// foreign key whose action is <c>SetNull</c> forces the column to accept null.
/// </description>
/// </item>
/// <item>
/// <description>
/// Every foreign key has an explicit index that covers it, and SQLite makes no index for a
/// foreign key at all. InnoDB creates a hidden index when a foreign key has none, and the index
/// listing would then differ between the engines for a reason that has nothing to do with the
/// contract. Covering each key makes both listings identical.
/// </description>
/// </item>
/// <item>
/// <description>
/// No column has a default value, no object has a comment, and no column is declared with a
/// boolean, decimal or date type. Those diverge — MySQL propagates a base table's constraints to a
/// view's columns, quotes a default as the engine wrote it, and hands back a decimal as a scaled
/// decimal and a date as a datetime where SQLite hands back a double and the text it stored — and
/// each divergence is asserted in the engine's own project instead.
/// </description>
/// </item>
/// <item>
/// <description>
/// <c>readings.ratio</c> is <c>DOUBLE</c> in MySQL and <c>REAL</c> in SQLite: SQLite folds every
/// declared numeric type into its own storage classes, so both hand the Provider a double and the
/// same JSON comes out.
/// </description>
/// </item>
/// </list>
/// </remarks>
public static class ContractSchema
{
    /// <summary>
    /// The script that seeds the shared schema in a MySQL database.
    /// </summary>
    /// <value>
    /// Statements the test fixture runs through a writable connection before handing the database
    /// to the Provider.
    /// </value>
    /// <remarks>
    /// The drops go in dependency order and the view goes first: a view can hold a dependency on a
    /// table, and MySQL refuses to drop what is still referenced.
    /// </remarks>
    public const string MySqlDdl = """
        DROP VIEW IF EXISTS active_users;
        DROP TABLE IF EXISTS bookings;
        DROP TABLE IF EXISTS memberships;
        DROP TABLE IF EXISTS composite;
        DROP TABLE IF EXISTS users;
        DROP TABLE IF EXISTS orgs;
        DROP TABLE IF EXISTS readings;

        CREATE TABLE orgs (
            id INT NOT NULL AUTO_INCREMENT,
            name VARCHAR(50) NOT NULL,
            PRIMARY KEY (id)
        );

        CREATE TABLE users (
            id INT NOT NULL AUTO_INCREMENT,
            org_id INT NULL,
            email VARCHAR(50),
            role VARCHAR(50) NOT NULL,
            PRIMARY KEY (id),
            CONSTRAINT fk_users_org FOREIGN KEY (org_id) REFERENCES orgs(id) ON DELETE CASCADE ON UPDATE SET NULL
        );
        CREATE INDEX idx_users_org_role ON users(org_id, role);
        CREATE UNIQUE INDEX idx_users_email ON users(email);

        CREATE TABLE composite (
            a VARCHAR(20) NOT NULL,
            b INT NOT NULL,
            PRIMARY KEY (b, a)
        );

        CREATE TABLE bookings (
            id INT NOT NULL AUTO_INCREMENT,
            composite_b INT NOT NULL,
            composite_a VARCHAR(20) NOT NULL,
            PRIMARY KEY (id),
            CONSTRAINT fk_bookings_parent FOREIGN KEY (composite_b, composite_a) REFERENCES composite(b, a) ON DELETE CASCADE
        );
        CREATE INDEX idx_bookings_parent ON bookings(composite_b, composite_a);

        CREATE TABLE memberships (
            id INT NOT NULL AUTO_INCREMENT,
            user_id INT NOT NULL,
            PRIMARY KEY (id),
            CONSTRAINT fk_memberships_user FOREIGN KEY (user_id) REFERENCES users(id)
        );
        CREATE INDEX idx_memberships_user ON memberships(user_id);

        CREATE TABLE readings (
            id INT NOT NULL AUTO_INCREMENT,
            label VARCHAR(50),
            ratio DOUBLE,
            payload VARBINARY(10),
            note VARCHAR(50),
            PRIMARY KEY (id)
        );
        INSERT INTO readings (label, ratio, payload, note) VALUES
            ('alpha', 1.5, X'0102', NULL),
            ('beta', -0.25, X'', 'hello'),
            ('gamma', 0, NULL, ''),
            ('delta', 2.5, NULL, 'four'),
            ('epsilon', 3.5, NULL, 'five'),
            ('zeta', 4.5, NULL, 'six');

        CREATE VIEW active_users AS SELECT id, email FROM users;

        INSERT INTO orgs (name) VALUES ('acme');
        INSERT INTO users (org_id, email, role) VALUES (1, 'a@x', 'admin');
        """;

    /// <summary>
    /// The script that seeds the shared schema in a SQLite database.
    /// </summary>
    /// <value>
    /// Statements the test fixture runs through a writable connection before handing the database
    /// to the Provider.
    /// </value>
    public const string SqliteDdl = """
        CREATE TABLE orgs (
            id INTEGER PRIMARY KEY NOT NULL,
            name TEXT NOT NULL
        );

        CREATE TABLE users (
            id INTEGER PRIMARY KEY NOT NULL,
            org_id INTEGER REFERENCES orgs(id) ON DELETE CASCADE ON UPDATE SET NULL,
            email TEXT,
            role TEXT NOT NULL
        );
        CREATE INDEX idx_users_org_role ON users(org_id, role);
        CREATE UNIQUE INDEX idx_users_email ON users(email);

        CREATE TABLE composite (
            a TEXT NOT NULL,
            b INTEGER NOT NULL,
            PRIMARY KEY (b, a)
        );

        CREATE TABLE bookings (
            id INTEGER PRIMARY KEY NOT NULL,
            composite_b INTEGER NOT NULL,
            composite_a TEXT NOT NULL,
            FOREIGN KEY (composite_b, composite_a) REFERENCES composite(b, a) ON DELETE CASCADE
        );
        CREATE INDEX idx_bookings_parent ON bookings(composite_b, composite_a);

        CREATE TABLE memberships (
            id INTEGER PRIMARY KEY NOT NULL,
            user_id INTEGER NOT NULL REFERENCES users(id)
        );
        CREATE INDEX idx_memberships_user ON memberships(user_id);

        CREATE TABLE readings (
            id INTEGER PRIMARY KEY NOT NULL,
            label TEXT,
            ratio REAL,
            payload BLOB,
            note TEXT
        );
        INSERT INTO readings (label, ratio, payload, note) VALUES
            ('alpha', 1.5, x'0102', NULL),
            ('beta', -0.25, zeroblob(0), 'hello'),
            ('gamma', 0.0, NULL, ''),
            ('delta', 2.5, NULL, 'four'),
            ('epsilon', 3.5, NULL, 'five'),
            ('zeta', 4.5, NULL, 'six');

        CREATE VIEW active_users AS SELECT id, email FROM users;

        INSERT INTO orgs (name) VALUES ('acme');
        INSERT INTO users (org_id, email, role) VALUES (1, 'a@x', 'admin');
        """;
}
