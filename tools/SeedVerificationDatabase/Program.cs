using Microsoft.Data.Sqlite;

// Creates the SQLite database `tools/verify-installed-tool.ps1` points the installed tool at.
//
// Why this exists: the harness's subject is the *installed* .NET global tool, which no test project
// can reach — they all run the host the repository just built. Something has to create a database
// first, and the repository must not gain a dependency on the product for that, so this small
// project owns its own Driver. See SeedVerificationDatabase.csproj for why it is a project rather
// than a file-based C# app, and why it is not in the solution.
//
// The schema exercises every part of the four tools: a table with a primary key, a second table
// with a foreign key and a covering index, a view, and rows to count.
//
// Usage: dotnet <SeedVerificationDatabase.dll> <database-path>

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: dotnet run --project tools/SeedVerificationDatabase -- <database-path>");
    return 2;
}

var path = Path.GetFullPath(args[0]);
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
File.Delete(path);

// `DECIMAL` is declared rather than `REAL` so the column type the tool reports is the one the
// schema declares, and the foreign key is covered by an explicit index so this schema and the
// MySQL fixture in tools/seed-verification-mysql.sql describe the same shape.
const string Schema = """
    CREATE TABLE users (
        id INTEGER PRIMARY KEY,
        name TEXT NOT NULL,
        email TEXT
    );
    CREATE TABLE orders (
        id INTEGER PRIMARY KEY,
        user_id INTEGER NOT NULL REFERENCES users (id),
        total DECIMAL NOT NULL
    );
    CREATE INDEX ix_orders_user_id ON orders (user_id);
    CREATE VIEW active_users AS SELECT id, name FROM users;
    INSERT INTO users (name, email) VALUES ('Alice', 'alice@example.com'), ('Bob', NULL);
    INSERT INTO orders (user_id, total) VALUES (1, 10.5), (2, 20.25);
    """;

using var connection = new SqliteConnection($"Data Source={path}");
connection.Open();

using var command = connection.CreateCommand();
command.CommandText = Schema;
command.ExecuteNonQuery();

// The path is printed as the last line so the harness can capture it.
Console.WriteLine(path);
return 0;
