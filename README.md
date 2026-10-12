# SharpDb

A read-only MCP server that lets an AI agent see what is in a relational database and query it, with no way to change it.

SharpDb runs as a local subprocess of your MCP client and speaks the Model Context Protocol over stdio. It exposes four tools:

| Tool | What it answers |
|---|---|
| `list_tables` | Which tables and views the database has. |
| `describe_table` | What one table looks like: columns, primary key, foreign keys, indexes. |
| `execute_query` | The rows a read-only SQL statement returns. |
| `list_databases` | Which databases the connection can see. Informational only. |

One install serves **MySQL**, **PostgreSQL**, **SQLite** and **Oracle**. Which one a process serves is decided at startup from its environment, not by installing a different package — so moving between databases never means reinstalling.

## Install

Installing needs the .NET 8 SDK or newer. Running needs only a runtime — .NET 8 or any later major.

```
dotnet tool install --global Holwon.SharpDb.Mcp
```

This puts `sharpdb-mcp` on your `PATH`. The package carries every database type, so you install once.

## Configure

Add a server entry to your MCP client's configuration — in `.vscode/mcp.json` for a workspace, or at user scope to have it everywhere.

**MySQL**

```json
{
  "servers": {
    "sharpdb-mysql": {
      "type": "stdio",
      "command": "sharpdb-mcp",
      "args": [],
      "env": {
        "SHARPDB__TYPE": "mysql",
        "SHARPDB__CONNECTIONSTRING": "Server=localhost;Port=3306;Database=app;User ID=sharpdb_ro;Password=CHANGE_ME"
      }
    }
  }
}
```

**SQLite**

```json
{
  "servers": {
    "sharpdb-sqlite": {
      "type": "stdio",
      "command": "sharpdb-mcp",
      "args": [],
      "env": {
        "SHARPDB__TYPE": "sqlite",
        "SHARPDB__CONNECTIONSTRING": "Data Source=C:\\data\\app.db"
      }
    }
  }
}
```

**PostgreSQL**

```json
{
  "servers": {
    "sharpdb-postgres": {
      "type": "stdio",
      "command": "sharpdb-mcp",
      "args": [],
      "env": {
        "SHARPDB__TYPE": "postgresql",
        "SHARPDB__CONNECTIONSTRING": "Host=localhost;Port=5432;Database=app;Username=sharpdb_ro;Password=CHANGE_ME"
      }
    }
  }
}
```

The connection string is an ordinary Npgsql one, so an existing one can be pasted in as it stands. Read-only is added by the server, not by the string: write the string you would give any Npgsql client, and SharpDb puts the session in the engine's read-only mode before it hands the connection to anything.

**Oracle**

```json
{
  "servers": {
    "sharpdb-oracle": {
      "type": "stdio",
      "command": "sharpdb-mcp",
      "args": [],
      "env": {
        "SHARPDB__TYPE": "oracle",
        "SHARPDB__CONNECTIONSTRING": "Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=XEPDB1)));User Id=sharpdb_ro;Password=CHANGE_ME"
      }
    }
  }
}
```

The connection string is an ordinary ODP.NET one, in either the full `(DESCRIPTION=…)` form or the short `host:port/service` form. Read-only is added by the server: it opens a transaction and puts it in `SET TRANSACTION READ ONLY`, because Oracle has no session-wide read-only mode. Write your SQL with `@name` placeholders as you would for any other engine — the server respells them as `:name` before Oracle sees them.

The `command` is the command the install put on your `PATH`, and `args` stays empty: everything that varies lives in the `env` block, so nothing about the server needs editing when the database changes.

The key names use a **double** underscore. That is the .NET configuration system's hierarchy separator; a single underscore spells a different variable, which the server silently does not read.

### What each type supports

| Type | Driver | Notes |
|---|---|---|
| `mysql` | MySqlConnector | — |
| `oracle` | Oracle.ManagedDataAccess.Core | Oracle Database 12c or later. Tested against XE 21; there is no multi-version test matrix. |
| `postgresql` | Npgsql | PostgreSQL 10 or later. Tested against 16; there is no multi-version test matrix. |
| `sqlite` | Microsoft.Data.Sqlite | — |

PostgreSQL's introspection reads `attidentity`, which is why 10 is the floor. Oracle's reads `IDENTITY_COLUMN`, which is why 12c is. The connection string needs no read-only parameter: what a session is put into is the server's business.

### What differs between the engines

Every tool works on every engine. What differs is the engine's own physics, and it is recorded here rather than smoothed over, because a caller that assumes otherwise gets a wrong answer that looks like a right one:

| Behaviour | MySQL | PostgreSQL | SQLite | Oracle |
|---|---|---|---|---|
| `''` is distinct from NULL | yes | yes | yes | **no — `''` is NULL** |
| `ON UPDATE` referential action | yes | yes | yes | **no — a syntax error** |
| Read-only enforcement | session | session | file | **transaction** |
| Read-only mode stops DDL | yes | yes | yes | **no — DDL implicitly commits** |
| The account's privileges are | advised | advised | not applicable | **required** |
| `EXPLAIN` returns a plan | yes | yes | yes | **no — engine reports a syntax error** |
| `SHOW` | yes | no — the engine reports a syntax error | no | **no — the engine reports a syntax error** |
| Error messages name the object | yes | yes | yes | **no** |
| `SELECT` with no `FROM` | yes | yes | yes | **no on 21c — a syntax error** |

Two of these matter most. On Oracle, `''` is NULL, so a comparison against an empty string matches nothing and a column written with one reads back as null; and Oracle's read-only mode is a property of a transaction, so a DDL statement commits and thereby escapes it — which is why the account's privileges are the boundary there rather than advice (see [ADR 0014](docs/adr/0014-oracle-read-only-transactional-with-ddl-hole.md)).

Oracle also folds an unquoted identifier to upper case, so a query that does not alias its columns answers with upper-case labels (`SELECT id …` gives a column named `ID`). Alias the columns — `SELECT id AS "id" …` — when the label matters.

### What a description reports about a view

`describe_table` answers with the columns the catalogue holds, and what a view's columns report differs by engine: MySQL propagates the base table's `NOT NULL` and defaults into the view's columns, while PostgreSQL and SQLite report neither. Read a view's `isNullable` and `defaultValue` as "what the catalogue says" rather than as "what the column behind it is".

## More than one database

A server process is bound to exactly one database. To give an agent two databases, add two entries — one server each:

```json
{
  "servers": {
    "sharpdb-app": {
      "type": "stdio",
      "command": "sharpdb-mcp",
      "args": [],
      "env": {
        "SHARPDB__TYPE": "mysql",
        "SHARPDB__CONNECTIONSTRING": "Server=localhost;Database=app;User ID=sharpdb_ro;Password=CHANGE_ME"
      }
    },
    "sharpdb-analytics": {
      "type": "stdio",
      "command": "sharpdb-mcp",
      "args": [],
      "env": {
        "SHARPDB__TYPE": "sqlite",
        "SHARPDB__CONNECTIONSTRING": "Data Source=C:\\data\\analytics.db"
      }
    }
  }
}
```

Each entry is its own process with its own copy of the four tools. This is the only way to reach a second database: no tool takes a database argument, and `list_databases` only reports what the connection can see.

## Configuration reference

Every value comes from the `env` block. There is no configuration file to look for and no command-line arguments.

| Key | Required | Default | Meaning |
|---|---|---|---|
| `SHARPDB__TYPE` | yes | — | `mysql`, `oracle`, `postgresql` or `sqlite`. The comparison ignores case and surrounding spaces. |
| `SHARPDB__CONNECTIONSTRING` | yes | — | Passed through to the Driver that serves this database type. |
| `SHARPDB__DEFAULTMAXROWS` | no | `100` | Rows returned when a query does not ask for a number. |
| `SHARPDB__MAXROWS` | no | `1000` | The ceiling a query cannot raise, only be cut down to. |
| `SHARPDB__TIMEOUTSECONDS` | no | `30` | How long a query may run before it is abandoned. |

## Read-only

Nothing writes to your database, and that is enforced in three places rather than one.

1. **The Guard.** Every statement the agent sends is parsed before it runs. Only `SELECT`, `EXPLAIN` and `SHOW` are allowed; anything else is refused before it reaches the database, and the refusal tells the agent what it may use instead.
2. **The engine's own read-only mode.** MySQL connections are opened with the session set read-only, PostgreSQL sessions are put in the engine's transaction-read-only mode, SQLite files are opened read-only, and Oracle connections are opened inside a read-only transaction. A statement the Guard somehow let through would still be refused by the database.
3. **The database account.** This is the boundary that actually holds.

### The account is the real boundary

The first two layers protect against mistakes and against a bug in the check. They are code, and code can have holes. The only limit that cannot be talked around is the privileges of the account you hand over.

Give SharpDb a dedicated account that can read and nothing else. For the database `app`:

```sql
CREATE USER 'sharpdb_ro'@'%' IDENTIFIED BY 'a-strong-password';
GRANT SELECT ON app.* TO 'sharpdb_ro'@'%';
```

`SELECT` is enough for all four tools, including reading views. No `INSERT`, `UPDATE`, `DELETE`, `CREATE`, `DROP`, `ALTER` or `FILE` — so even if something were let through, the database would refuse it.

On Oracle this layer is not advice but the boundary: the engine's read-only mode does not stop a DDL statement, so only a `SELECT`-only account keeps one out (see [ADR 0014](docs/adr/0014-oracle-read-only-transactional-with-ddl-hole.md)).

For SQLite there is no account to restrict: the file's own permissions are the boundary, so point SharpDb at a copy or a read-only path if the file matters.

### What the server never exposes

- The connection string is never logged, at any log level. Nothing that could carry a password reaches the output.
- Row values sent as query parameters are never logged. The statement itself is logged, so you can see what was asked.
- Startup failures name the reason without repeating the connection string.

## Logging

SharpDb logs to stderr — stdout belongs to the protocol — using the standard .NET logging key:

```json
"env": {
  "SHARPDB__TYPE": "sqlite",
  "SHARPDB__CONNECTIONSTRING": "Data Source=C:\\data\\app.db",
  "Logging__LogLevel__Default": "Debug"
}
```

`Information` is the default. Raise it to `Debug` when something is not being called as you expect; lower it to `Warning` to keep the client's log quiet. Statements are logged at `Information`, and a call that fails is logged at `Warning`.

## When it will not start

SharpDb opens one connection while starting and **exits immediately** if anything is wrong, writing a single line to stderr that your client shows you. A server that starts and then fails on every call is much harder to diagnose, so this is deliberate.

| What you see | What it means |
|---|---|
| `sharpdb: SHARPDB__TYPE is required (expected "mysql" or "oracle" or "postgresql" or "sqlite").` | The `env` block is missing the type, or spells it with a single underscore. |
| `sharpdb: SHARPDB__TYPE 'postgres' is unknown (expected "mysql" or "oracle" or "postgresql" or "sqlite").` | A type was given that no database type serves. `postgres` is not an alias; the type is spelled `postgresql`. |
| `sharpdb: SHARPDB__CONNECTIONSTRING is required and must not be blank.` | The connection string is missing or empty. |
| `sharpdb: Could not connect to the mysql database: …` | The server could not connect — usually credentials, host or database name. |
| `sharpdb: Invalid configuration: Failed to convert configuration value 'plenty' at 'SHARPDB:MaxRows' to type 'System.Int32'.` | A row limit or the timeout is not a number. |

The reason is always one line, it always starts with `sharpdb: `, and it never includes the connection string.

## License

SharpDb is licensed under the [GNU General Public License, version 3](LICENSE) (GPL-3.0).

You may use it commercially. If you distribute it, or anything built on it — including a wrapper around it — you must publish that work's source under the same licence.

This project uses Oracle Managed Data Access, licensed under Oracle Free Distribution Terms. See [NOTICE](NOTICE) for the third-party licence notice.
