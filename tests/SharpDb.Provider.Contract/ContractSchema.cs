namespace SharpDb.Provider.Contract;

/// <summary>
/// The one database every engine seeds, written in each engine's own SQL, so the shared suites can
/// assert the same answers against any Provider.
/// </summary>
/// <remarks>
/// The scripts describe the same structure — and the differences between them are the whole
/// point of this file, because every one of them was forced by a measured engine behaviour or a
/// measured divergence between the engines.
/// <para>
/// The rule the shape follows: <em>what the shared suites assert must be answerable identically by
/// every engine.</em> Where an engine cannot express something — or expresses it in a way that
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
/// contract. PostgreSQL creates none either, so the same rule covers it. Covering each key makes
/// every listing identical.
/// </description>
/// </item>
/// <item>
/// <description>
/// No column has a default value, no object has a comment, and no column is declared with a
/// boolean, decimal or date type. Those diverge — MySQL propagates a base table's constraints to a
/// view's columns, quotes a default as the engine wrote it, and hands back a decimal as a scaled
/// decimal and a date as a datetime where SQLite hands back a double and the text it stored, while
/// PostgreSQL propagates none of a view's base constraints — and each divergence is asserted in
/// the engine's own project instead.
/// </description>
/// </item>
/// <item>
/// <description>
/// The composite foreign key on <c>bookings</c> declares its <c>ON UPDATE</c> rule explicitly —
/// <c>NO ACTION</c> — rather than letting it default. Every engine's default is a behaviour of
/// that engine: MariaDB reports <c>RESTRICT</c> for an omitted clause where MySQL, PostgreSQL,
/// SQLite and SQL Server report <c>NO ACTION</c>. Declaring the rule makes the shared assertion
/// read the schema rather than an engine's default.
/// </description>
/// </item>
/// <item>
/// <description>
/// <c>readings.ratio</c> is <c>DOUBLE</c> in MySQL, <c>REAL</c> in SQLite and
/// <c>DOUBLE PRECISION</c> in PostgreSQL: SQLite folds every declared numeric type into its own
/// storage classes, so all three hand the Provider a double and the same JSON comes out.
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
            CONSTRAINT fk_bookings_parent FOREIGN KEY (composite_b, composite_a) REFERENCES composite(b, a) ON DELETE CASCADE ON UPDATE NO ACTION
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
            FOREIGN KEY (composite_b, composite_a) REFERENCES composite(b, a) ON DELETE CASCADE ON UPDATE NO ACTION
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

    /// <summary>
    /// The script that seeds the shared schema in a PostgreSQL database.
    /// </summary>
    /// <value>
    /// Statements the test fixture runs through a writable connection before handing the database
    /// to the Provider.
    /// </value>
    /// <remarks>
    /// The third script of one structure, and the shape it shares with the other two is the point.
    /// Where it had to differ, the reason was measured rather than chosen:
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// The keys are written <c>GENERATED BY DEFAULT AS IDENTITY</c> rather than <c>serial</c>. Both
    /// are auto-increment to a caller, and the two are recognised by different evidence —
    /// <c>serial</c> leaves a <c>nextval</c> default and no flag, identity sets
    /// <c>attidentity</c> — so the shared suite pinning one spelling would leave the other covered
    /// only in the engine's own project. Identity is used here because the shared assertions read
    /// the flag that arrives with it; the <c>serial</c> spelling is asserted in PostgreSQL's own
    /// tests.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>readings.payload</c> is <c>BYTEA</c>, written with the <c>\x</c> hex form. An empty
    /// <c>bytea</c> is written as <c>''::bytea</c> rather than as a zero-length blob literal,
    /// because PostgreSQL has no such literal and the shared row needs a value that is empty
    /// rather than absent.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>readings.ratio</c> is <c>DOUBLE PRECISION</c>, which is what MySQL's <c>DOUBLE</c> and
    /// SQLite's <c>REAL</c> both mean to this Provider: all three hand back a double, so the same
    /// JSON comes out.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Every foreign key has an explicit index covering it, which is the same rule the other two
    /// scripts follow and for a stronger reason here: measured, PostgreSQL creates no index for a
    /// foreign key, so without these the index listing would show only the primary keys.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// No column has a default value and no object has a comment, for the same reason the other
    /// two scripts keep them out: the engines report what a view's columns inherit differently.
    /// Measured on PostgreSQL 16.15, a view's columns report neither the base table's
    /// <c>NOT NULL</c> nor its default, so those are asserted in the engine's own project.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    public const string PostgreSqlDdl = """
        DROP VIEW IF EXISTS active_users;
        DROP TABLE IF EXISTS bookings;
        DROP TABLE IF EXISTS memberships;
        DROP TABLE IF EXISTS composite;
        DROP TABLE IF EXISTS users;
        DROP TABLE IF EXISTS orgs;
        DROP TABLE IF EXISTS readings;

        CREATE TABLE orgs (
            id INT GENERATED BY DEFAULT AS IDENTITY,
            name VARCHAR(50) NOT NULL,
            PRIMARY KEY (id)
        );

        CREATE TABLE users (
            id INT GENERATED BY DEFAULT AS IDENTITY,
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
            id INT GENERATED BY DEFAULT AS IDENTITY,
            composite_b INT NOT NULL,
            composite_a VARCHAR(20) NOT NULL,
            PRIMARY KEY (id),
            CONSTRAINT fk_bookings_parent FOREIGN KEY (composite_b, composite_a)
                REFERENCES composite(b, a) ON DELETE CASCADE ON UPDATE NO ACTION
        );
        CREATE INDEX idx_bookings_parent ON bookings(composite_b, composite_a);

        CREATE TABLE memberships (
            id INT GENERATED BY DEFAULT AS IDENTITY,
            user_id INT NOT NULL,
            PRIMARY KEY (id),
            CONSTRAINT fk_memberships_user FOREIGN KEY (user_id) REFERENCES users(id)
        );
        CREATE INDEX idx_memberships_user ON memberships(user_id);

        CREATE TABLE readings (
            id INT GENERATED BY DEFAULT AS IDENTITY,
            label VARCHAR(50),
            ratio DOUBLE PRECISION,
            payload BYTEA,
            note VARCHAR(50),
            PRIMARY KEY (id)
        );
        INSERT INTO readings (label, ratio, payload, note) VALUES
            ('alpha', 1.5, '\x0102'::bytea, NULL),
            ('beta', -0.25, ''::bytea, 'hello'),
            ('gamma', 0, NULL, ''),
            ('delta', 2.5, NULL, 'four'),
            ('epsilon', 3.5, NULL, 'five'),
            ('zeta', 4.5, NULL, 'six');

        CREATE VIEW active_users AS SELECT id, email FROM users;

        INSERT INTO orgs (name) VALUES ('acme');
        INSERT INTO users (org_id, email, role) VALUES (1, 'a@x', 'admin');
        """;

    /// <summary>
    /// The statements that seed the shared schema in an Oracle schema, in the order they must run.
    /// </summary>
    /// <value>
    /// Statements the test fixture runs through a writable connection before handing the schema to
    /// the Provider.
    /// </value>
    /// <remarks>
    /// A list rather than one script, because Oracle's Driver executes exactly one statement per
    /// round trip — there is no multi-statement form to send. Every other engine's script is one
    /// string; this one cannot be.
    /// <para>
    /// The identifiers are written unquoted, so Oracle stores them in upper case and a caller
    /// reaches them by writing the lower-case name it folds from. Quoting them to keep the case
    /// would give the dictionary the lower-case names the shared assertions expect, and would also
    /// make every unquoted reference in the shared query suite fail — so the Provider reports a
    /// catalogue name that is all upper case in lower case instead (see
    /// <c>OracleDatabaseProvider</c>).
    /// </para>
    /// <para>
    /// Where it had to differ from the other three, the reason was measured:
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// The foreign keys declare <c>ON DELETE</c> and nothing else. Oracle has no <c>ON UPDATE</c>
    /// referential action at all — declaring one is <c>ORA-00905</c> — so the shared key cannot
    /// carry one, and the engines that can assert it do so in their own projects.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>gamma</c>'s <c>note</c> is <c>NULL</c> rather than the empty string, because Oracle
    /// stores <c>''</c> as NULL: measured, <c>v = ''</c> matches no row and <c>v IS NULL</c> matches
    /// one. The engines that keep an empty string apart from NULL assert it in their own projects.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>readings.payload</c> is <c>RAW(10)</c>, and <c>beta</c>'s empty payload is written as
    /// <c>NULL</c> rather than as <c>HEXTORAW('')</c>, which Oracle would store as NULL in any
    /// case: Oracle has no zero-length blob literal. The empty blob is asserted in the engines that
    /// can hold one.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// The keys are declared <c>GENERATED BY DEFAULT AS IDENTITY</c>, which is what
    /// <c>ALL_TAB_COLUMNS.IDENTITY_COLUMN</c> reports and therefore what the Provider reads.
    /// </description>
    /// </item>
    /// </list>
    /// </para>
    /// </remarks>
    public static readonly string[] OracleDdl =
    [
        """
        CREATE TABLE orgs (
            id NUMBER GENERATED BY DEFAULT AS IDENTITY,
            name VARCHAR2(50) NOT NULL,
            CONSTRAINT orgs_pk PRIMARY KEY (id)
        )
        """,
        """
        CREATE TABLE users (
            id NUMBER GENERATED BY DEFAULT AS IDENTITY,
            org_id NUMBER NULL,
            email VARCHAR2(50),
            role VARCHAR2(50) NOT NULL,
            CONSTRAINT users_pk PRIMARY KEY (id),
            CONSTRAINT fk_users_org FOREIGN KEY (org_id) REFERENCES orgs(id) ON DELETE CASCADE
        )
        """,
        "CREATE INDEX idx_users_org_role ON users(org_id, role)",
        "CREATE UNIQUE INDEX idx_users_email ON users(email)",
        """
        CREATE TABLE composite (
            a VARCHAR2(20) NOT NULL,
            b NUMBER NOT NULL,
            CONSTRAINT composite_pk PRIMARY KEY (b, a)
        )
        """,
        """
        CREATE TABLE bookings (
            id NUMBER GENERATED BY DEFAULT AS IDENTITY,
            composite_b NUMBER NOT NULL,
            composite_a VARCHAR2(20) NOT NULL,
            CONSTRAINT bookings_pk PRIMARY KEY (id),
            CONSTRAINT fk_bookings_parent FOREIGN KEY (composite_b, composite_a)
                REFERENCES composite(b, a) ON DELETE CASCADE
        )
        """,
        "CREATE INDEX idx_bookings_parent ON bookings(composite_b, composite_a)",
        """
        CREATE TABLE memberships (
            id NUMBER GENERATED BY DEFAULT AS IDENTITY,
            user_id NUMBER NOT NULL,
            CONSTRAINT memberships_pk PRIMARY KEY (id),
            CONSTRAINT fk_memberships_user FOREIGN KEY (user_id) REFERENCES users(id)
        )
        """,
        "CREATE INDEX idx_memberships_user ON memberships(user_id)",
        """
        CREATE TABLE readings (
            id NUMBER GENERATED BY DEFAULT AS IDENTITY,
            label VARCHAR2(50),
            ratio NUMBER,
            payload RAW(10),
            note VARCHAR2(50),
            CONSTRAINT readings_pk PRIMARY KEY (id)
        )
        """,
        "INSERT INTO readings (label, ratio, payload, note) VALUES ('alpha', 1.5, HEXTORAW('0102'), NULL)",
        "INSERT INTO readings (label, ratio, payload, note) VALUES ('beta', -0.25, NULL, 'hello')",
        "INSERT INTO readings (label, ratio, payload, note) VALUES ('gamma', 0, NULL, NULL)",
        "INSERT INTO readings (label, ratio, payload, note) VALUES ('delta', 2.5, NULL, 'four')",
        "INSERT INTO readings (label, ratio, payload, note) VALUES ('epsilon', 3.5, NULL, 'five')",
        "INSERT INTO readings (label, ratio, payload, note) VALUES ('zeta', 4.5, NULL, 'six')",
        "CREATE VIEW active_users AS SELECT id, email FROM users",
        "INSERT INTO orgs (name) VALUES ('acme')",
        "INSERT INTO users (org_id, email, role) VALUES (1, 'a@x', 'admin')",
    ];

    /// <summary>
    /// The batches that seed the shared schema in a SQL Server database, in the order they must run.
    /// </summary>
    /// <value>
    /// Batches the test fixture runs through a writable connection before handing the database to
    /// the Provider.
    /// </value>
    /// <remarks>
    /// A list rather than one script, and for a reason that is this engine's own: in T-SQL
    /// <c>CREATE VIEW</c> must be the first statement of its batch, so a view cannot share one with
    /// the drops and the tables that precede it. There is no multi-batch separator to write instead
    /// — <c>GO</c> is a client directive rather than T-SQL, and sending it to the server is a
    /// syntax error — so the boundaries are the fixture's, which is what this list is.
    /// <para>
    /// Where it had to differ from the other engines' scripts, the reason was measured:
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// The drops are guarded by <c>DROP … IF EXISTS</c> and each kind is dropped before the kind it
    /// depends on, because T-SQL refuses to drop a table a foreign key still points at.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>readings.ratio</c> is <c>FLOAT</c>, which the Driver hands back as a double, so the same
    /// JSON comes out as on MySQL, SQLite and PostgreSQL.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// No column carries a default and no object carries a comment, exactly as on the other
    /// engines: the shared schema leaves out what the engines answer differently, and this engine's
    /// own defaults and comments are asserted in its own project.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// The foreign keys declare both rules where the shared schema asks for both. Unlike Oracle,
    /// this engine has <c>ON UPDATE</c> actions, so the shared key carries its
    /// <c>ON UPDATE SET NULL</c> and the assertion that names it stays in the shared suite.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Every foreign key has an explicit index that covers it, as on every other engine: this one
    /// creates no index of its own for a foreign key, so the index listings would differ for a
    /// reason that has nothing to do with the contract.
    /// </description>
    /// </item>
    /// </list>
    /// </para>
    /// </remarks>
    public static readonly string[] MsSqlDdl =
    [
        """
        DROP VIEW IF EXISTS dbo.active_users;
        DROP TABLE IF EXISTS dbo.bookings;
        DROP TABLE IF EXISTS dbo.memberships;
        DROP TABLE IF EXISTS dbo.composite;
        DROP TABLE IF EXISTS dbo.users;
        DROP TABLE IF EXISTS dbo.orgs;
        DROP TABLE IF EXISTS dbo.readings;
        """,
        """
        CREATE TABLE dbo.orgs (
            id INT IDENTITY(1,1) NOT NULL,
            name VARCHAR(50) NOT NULL,
            CONSTRAINT pk_orgs PRIMARY KEY (id)
        )
        """,
        """
        CREATE TABLE dbo.users (
            id INT IDENTITY(1,1) NOT NULL,
            org_id INT NULL,
            email VARCHAR(50) NULL,
            role VARCHAR(50) NOT NULL,
            CONSTRAINT pk_users PRIMARY KEY (id),
            CONSTRAINT fk_users_org FOREIGN KEY (org_id) REFERENCES dbo.orgs(id)
                ON DELETE CASCADE ON UPDATE SET NULL
        );
        CREATE INDEX idx_users_org_role ON dbo.users(org_id, role);
        CREATE UNIQUE INDEX idx_users_email ON dbo.users(email);
        """,
        """
        CREATE TABLE dbo.composite (
            a VARCHAR(20) NOT NULL,
            b INT NOT NULL,
            CONSTRAINT pk_composite PRIMARY KEY (b, a)
        )
        """,
        """
        CREATE TABLE dbo.bookings (
            id INT IDENTITY(1,1) NOT NULL,
            composite_b INT NOT NULL,
            composite_a VARCHAR(20) NOT NULL,
            CONSTRAINT pk_bookings PRIMARY KEY (id),
            CONSTRAINT fk_bookings_parent FOREIGN KEY (composite_b, composite_a)
                REFERENCES dbo.composite(b, a) ON DELETE CASCADE ON UPDATE NO ACTION
        );
        CREATE INDEX idx_bookings_parent ON dbo.bookings(composite_b, composite_a);
        """,
        """
        CREATE TABLE dbo.memberships (
            id INT IDENTITY(1,1) NOT NULL,
            user_id INT NOT NULL,
            CONSTRAINT pk_memberships PRIMARY KEY (id),
            CONSTRAINT fk_memberships_user FOREIGN KEY (user_id) REFERENCES dbo.users(id)
        );
        CREATE INDEX idx_memberships_user ON dbo.memberships(user_id);
        """,
        """
        CREATE TABLE dbo.readings (
            id INT IDENTITY(1,1) NOT NULL,
            label VARCHAR(50) NULL,
            ratio FLOAT NULL,
            payload VARBINARY(10) NULL,
            note VARCHAR(50) NULL,
            CONSTRAINT pk_readings PRIMARY KEY (id)
        );
        INSERT INTO dbo.readings (label, ratio, payload, note) VALUES
            ('alpha', 1.5, 0x0102, NULL),
            ('beta', -0.25, 0x, 'hello'),            ('gamma', 0, NULL, ''),
            ('delta', 2.5, NULL, 'four'),
            ('epsilon', 3.5, NULL, 'five'),
            ('zeta', 4.5, NULL, 'six');
        """,
        "CREATE VIEW dbo.active_users AS SELECT id, email FROM dbo.users",
        """
        INSERT INTO dbo.orgs (name) VALUES ('acme');
        INSERT INTO dbo.users (org_id, email, role) VALUES (1, 'a@x', 'admin');
        """,
    ];
}
