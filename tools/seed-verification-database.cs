// Creates the SQLite database `tools/verify-installed-tool.ps1` points the installed tool at.
//
// Run via `dotnet run tools/seed-verification-database.cs -- <path>`: a file-based app, so nothing
// in the repository gains a package reference, and the SQLite database is produced without the
// tool's build output — which is what lets the verification claim to touch nothing SharpDb ships.
// File-based apps need the .NET 10 SDK or newer.
//
// The schema exercises every part of the four tools: a table with a primary key, a second table
// with a foreign key and a covering index, a view, and rows to count.

#:package Microsoft.Data.Sqlite@10.0.12

using Microsoft.Data.Sqlite;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: dotnet run tools/seed-verification-database.cs -- <database-path>");
    return 2;
}

var path = Path.GetFullPath(args[0]);
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
File.Delete(path);

using var connection = new SqliteConnection($"Data Source={path}");
connection.Open();

using var command = connection.CreateCommand();
command.CommandText = """
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
command.ExecuteNonQuery();

Console.WriteLine(path);
return 0;
