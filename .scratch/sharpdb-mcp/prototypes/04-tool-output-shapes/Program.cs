// PROTOTYPE — throwaway, not production code.
// Answers: "What should each MCP tool's output look like on the wire?"
// Run: dotnet run

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
};

var jsonOptionsCompact = new JsonSerializerOptions
{
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
};

// ────────────────────────────────────────────────────────────────────────────
// 1. execute_query — ~10 rows, mixed column types
// ────────────────────────────────────────────────────────────────────────────

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("  TOOL 1: execute_query");
Console.WriteLine("═══════════════════════════════════════════════════════════════");

// Sample data: 10 rows with int, string, nullable string, datetime, decimal, blob, bool
var queryResultRows = new List<Dictionary<string, object?>>
{
    new() { ["id"] = 1, ["name"] = "Alice Wang",    ["email"] = "alice@example.com",   ["created_at"] = "2024-01-15T08:30:00Z", ["balance"] = 1250.50m,  ["avatar"] = "iVBORw0KGgo=", ["is_active"] = true },
    new() { ["id"] = 2, ["name"] = "Bob Chen",      ["email"] = "bob@example.com",     ["created_at"] = "2024-02-20T14:15:00Z", ["balance"] = 3400.00m,  ["avatar"] = (object?)null, ["is_active"] = true },
    new() { ["id"] = 3, ["name"] = "Carol Li",      ["email"] = null,                  ["created_at"] = "2024-03-10T09:45:00Z", ["balance"] = 0.00m,     ["avatar"] = (object?)null, ["is_active"] = false },
    new() { ["id"] = 4, ["name"] = "David Zhang",   ["email"] = "david@example.com",   ["created_at"] = "2024-04-05T16:00:00Z", ["balance"] = 8920.75m,  ["avatar"] = "R0lGODlhAQABAIA=", ["is_active"] = true },
    new() { ["id"] = 5, ["name"] = "Eve Liu",       ["email"] = "eve@example.com",     ["created_at"] = "2024-05-12T11:20:00Z", ["balance"] = 450.25m,   ["avatar"] = (object?)null, ["is_active"] = true },
    new() { ["id"] = 6, ["name"] = "Frank Wu",      ["email"] = null,                  ["created_at"] = "2024-06-18T07:10:00Z", ["balance"] = 15000.00m, ["avatar"] = (object?)null, ["is_active"] = true },
    new() { ["id"] = 7, ["name"] = "Grace Huang",   ["email"] = "grace@example.com",   ["created_at"] = "2024-07-22T13:55:00Z", ["balance"] = 670.80m,   ["avatar"] = "iVBORw0KGgo=", ["is_active"] = false },
    new() { ["id"] = 8, ["name"] = "Henry Zhao",    ["email"] = "henry@example.com",   ["created_at"] = "2024-08-30T10:05:00Z", ["balance"] = 2100.00m,  ["avatar"] = (object?)null, ["is_active"] = true },
    new() { ["id"] = 9, ["name"] = "Iris Sun",      ["email"] = "iris@example.com",    ["created_at"] = "2024-09-14T15:40:00Z", ["balance"] = 55.99m,    ["avatar"] = (object?)null, ["is_active"] = true },
    new() { ["id"] = 10, ["name"] = "Jack Ma",      ["email"] = null,                  ["created_at"] = "2024-10-01T12:00:00Z", ["balance"] = 99999.99m, ["avatar"] = "R0lGODlhAQABAIA=", ["is_active"] = true },
};

// Format A: Array of objects
var formatA = JsonSerializer.Serialize(queryResultRows, jsonOptions);
Console.WriteLine("\n── Format A: Array of objects ──");
Console.WriteLine(formatA);

// Format B: {columns, rows} envelope
var columns = new[] { "id", "name", "email", "created_at", "balance", "avatar", "is_active" };
var rows = queryResultRows.Select(r => columns.Select(c => r[c]).ToArray()).ToArray();
var formatB = JsonSerializer.Serialize(new { columns, rows }, jsonOptions);
Console.WriteLine("\n── Format B: {columns, rows} envelope ──");
Console.WriteLine(formatB);

// Format C: Compact array (first row = header)
var compactRows = new List<object[]> { columns.Cast<object>().ToArray() };
foreach (var r in queryResultRows)
    compactRows.Add(columns.Select(c => r[c] ?? (object)"__NULL__").ToArray());
var formatC = JsonSerializer.Serialize(compactRows, jsonOptions);
Console.WriteLine("\n── Format C: Compact array (header row) ──");
Console.WriteLine(formatC);

// ────────────────────────────────────────────────────────────────────────────
// 2. list_tables
// ────────────────────────────────────────────────────────────────────────────

Console.WriteLine("\n═══════════════════════════════════════════════════════════════");
Console.WriteLine("  TOOL 2: list_tables");
Console.WriteLine("═══════════════════════════════════════════════════════════════");

var tables = new[]
{
    new { name = "users",        type = "table", comment = (string?)"User accounts" },
    new { name = "orders",       type = "table", comment = (string?)"Customer orders" },
    new { name = "order_items",  type = "table", comment = (string?)null },
    new { name = "products",     type = "table", comment = (string?)"Product catalog" },
    new { name = "user_summary", type = "view",  comment = (string?)null },
};
var listTablesJson = JsonSerializer.Serialize(tables, jsonOptions);
Console.WriteLine(listTablesJson);

// ────────────────────────────────────────────────────────────────────────────
// 3. describe_table
// ────────────────────────────────────────────────────────────────────────────

Console.WriteLine("\n═══════════════════════════════════════════════════════════════");
Console.WriteLine("  TOOL 3: describe_table");
Console.WriteLine("═══════════════════════════════════════════════════════════════");

var describeTable = new
{
    name = "orders",
    type = "table",
    comment = (string?)"Customer orders",
    columns = new[]
    {
        new { name = "id",          ordinalPosition = 1, dataType = "int",      fullDataType = (string?)null, isNullable = false, defaultValue = (string?)null, isAutoIncrement = true,  comment = (string?)null },
        new { name = "user_id",     ordinalPosition = 2, dataType = "int",      fullDataType = (string?)null, isNullable = false, defaultValue = (string?)null, isAutoIncrement = false, comment = (string?)null },
        new { name = "product_id",  ordinalPosition = 3, dataType = "int",      fullDataType = (string?)null, isNullable = false, defaultValue = (string?)null, isAutoIncrement = false, comment = (string?)null },
        new { name = "quantity",    ordinalPosition = 4, dataType = "int",      fullDataType = (string?)null, isNullable = false, defaultValue = "1",         isAutoIncrement = false, comment = (string?)null },
        new { name = "unit_price",  ordinalPosition = 5, dataType = "decimal",  fullDataType = "decimal(10,2)", isNullable = false, defaultValue = (string?)null, isAutoIncrement = false, comment = (string?)null },
        new { name = "status",      ordinalPosition = 6, dataType = "varchar",  fullDataType = "varchar(20)",   isNullable = false, defaultValue = "'pending'", isAutoIncrement = false, comment = (string?)null },
        new { name = "notes",       ordinalPosition = 7, dataType = "text",     fullDataType = (string?)null, isNullable = true,  defaultValue = (string?)null, isAutoIncrement = false, comment = (string?)null },
        new { name = "created_at",  ordinalPosition = 8, dataType = "datetime", fullDataType = (string?)null, isNullable = false, defaultValue = "CURRENT_TIMESTAMP", isAutoIncrement = false, comment = (string?)null },
    },
    primaryKey = new { constraintName = "PRIMARY", columns = new[] { "id" } },
    foreignKeys = new[]
    {
        new { name = "fk_orders_user",    localColumns = new[] { "user_id" },    referencedTable = "users",    referencedColumns = new[] { "id" }, onUpdate = "RESTRICT", onDelete = "CASCADE" },
        new { name = "fk_orders_product", localColumns = new[] { "product_id" }, referencedTable = "products", referencedColumns = new[] { "id" }, onUpdate = "RESTRICT", onDelete = "RESTRICT" },
    },
    indexes = new[]
    {
        new { name = "PRIMARY",          isUnique = true,  columns = new[] { "id" } },
        new { name = "idx_orders_user",  isUnique = false, columns = new[] { "user_id" } },
        new { name = "idx_orders_date",  isUnique = false, columns = new[] { "created_at" } },
    },
};
var describeTableJson = JsonSerializer.Serialize(describeTable, jsonOptions);
Console.WriteLine(describeTableJson);

// ────────────────────────────────────────────────────────────────────────────
// 4. Error shapes
// ────────────────────────────────────────────────────────────────────────────

Console.WriteLine("\n═══════════════════════════════════════════════════════════════");
Console.WriteLine("  ERRORS");
Console.WriteLine("═══════════════════════════════════════════════════════════════");

var errorWrite = new { error = "Statement rejected: INSERT is not allowed. Only SELECT, EXPLAIN and SHOW statements are permitted." };
var errorSyntax = new { error = "SQL syntax error: near \"SELEC\": syntax error at line 1, column 1" };
var errorTimeout = new { error = "Query execution timed out after 30 seconds. Consider adding LIMIT or optimizing the query." };

Console.WriteLine("\n── Error: rejected write ──");
Console.WriteLine(JsonSerializer.Serialize(errorWrite, jsonOptions));
Console.WriteLine("\n── Error: SQL syntax error ──");
Console.WriteLine(JsonSerializer.Serialize(errorSyntax, jsonOptions));
Console.WriteLine("\n── Error: timeout ──");
Console.WriteLine(JsonSerializer.Serialize(errorTimeout, jsonOptions));

// ────────────────────────────────────────────────────────────────────────────
// 5. Token cost comparison (execute_query formats)
// ────────────────────────────────────────────────────────────────────────────

Console.WriteLine("\n═══════════════════════════════════════════════════════════════");
Console.WriteLine("  TOKEN COST COMPARISON (execute_query, 10 rows)");
Console.WriteLine("═══════════════════════════════════════════════════════════════");

// Compact (no indent) versions
var formatACompact = JsonSerializer.Serialize(queryResultRows, jsonOptionsCompact);
var formatBCompact = JsonSerializer.Serialize(new { columns, rows }, jsonOptionsCompact);
var formatCCompact = JsonSerializer.Serialize(compactRows, jsonOptionsCompact);

static int ApproxTokens(string json)
{
    // Rough approximation: ~4 chars per token for English/JSON
    // This is a heuristic; actual token counts vary by model/tokenizer
    return (int)Math.Ceiling(json.Length / 4.0);
}

Console.WriteLine($"\n{"Format",-35} {"Chars",8} {"~Tokens",8} {"Pretty Chars",13} {"Pretty ~Tokens",15}");
Console.WriteLine(new string('─', 85));
Console.WriteLine($"{"A: Array of objects",-35} {formatACompact.Length,8} {ApproxTokens(formatACompact),8} {formatA.Length,13} {ApproxTokens(formatA),15}");
Console.WriteLine($"{"B: {columns, rows} envelope",-35} {formatBCompact.Length,8} {ApproxTokens(formatBCompact),8} {formatB.Length,13} {ApproxTokens(formatB),15}");
Console.WriteLine($"{"C: Compact array (header)",-35} {formatCCompact.Length,8} {ApproxTokens(formatCCompact),8} {formatC.Length,13} {ApproxTokens(formatC),15}");

// Also measure list_tables and describe_table
var listTablesCompact = JsonSerializer.Serialize(tables, jsonOptionsCompact);
var describeTableCompact = JsonSerializer.Serialize(describeTable, jsonOptionsCompact);

Console.WriteLine($"\n{"Other tools",-35} {"Chars",8} {"~Tokens",8}");
Console.WriteLine(new string('─', 55));
Console.WriteLine($"{"list_tables",-35} {listTablesCompact.Length,8} {ApproxTokens(listTablesCompact),8}");
Console.WriteLine($"{"describe_table",-35} {describeTableCompact.Length,8} {ApproxTokens(describeTableCompact),8}");

// ────────────────────────────────────────────────────────────────────────────
// 6. Scale test: what happens at 100 rows?
// ────────────────────────────────────────────────────────────────────────────

Console.WriteLine("\n═══════════════════════════════════════════════════════════════");
Console.WriteLine("  SCALE TEST: 100 rows (execute_query)");
Console.WriteLine("═══════════════════════════════════════════════════════════════");

var bigRows = new List<Dictionary<string, object?>>();
for (int i = 1; i <= 100; i++)
{
    bigRows.Add(new()
    {
        ["id"] = i,
        ["name"] = $"User {i:D3}",
        ["email"] = i % 3 == 0 ? null : $"user{i}@example.com",
        ["created_at"] = $"2024-{(i % 12) + 1:D2}-{(i % 28) + 1:D2}T12:00:00Z",
        ["balance"] = Math.Round(i * 100.5m, 2),
        ["avatar"] = i % 5 == 0 ? "iVBORw0KGgo=" : null,
        ["is_active"] = i % 4 != 0,
    });
}

var bigA = JsonSerializer.Serialize(bigRows, jsonOptionsCompact);
var bigColumns = columns;
var bigRowsArr = bigRows.Select(r => bigColumns.Select(c => r[c]).ToArray()).ToArray();
var bigB = JsonSerializer.Serialize(new { columns = bigColumns, rows = bigRowsArr }, jsonOptionsCompact);
var bigCompact = new List<object[]> { bigColumns.Cast<object>().ToArray() };
foreach (var r in bigRows)
    bigCompact.Add(bigColumns.Select(c => r[c] ?? (object)"__NULL__").ToArray());
var bigC = JsonSerializer.Serialize(bigCompact, jsonOptionsCompact);

Console.WriteLine($"\n{"Format",-35} {"Chars",8} {"~Tokens",8}");
Console.WriteLine(new string('─', 55));
Console.WriteLine($"{"A: Array of objects",-35} {bigA.Length,8} {ApproxTokens(bigA),8}");
Console.WriteLine($"{"B: {columns, rows} envelope",-35} {bigB.Length,8} {ApproxTokens(bigB),8}");
Console.WriteLine($"{"C: Compact array (header)",-35} {bigC.Length,8} {ApproxTokens(bigC),8}");

// ────────────────────────────────────────────────────────────────────────────
// 7. UseStructuredContent note
// ────────────────────────────────────────────────────────────────────────────

Console.WriteLine("\n═══════════════════════════════════════════════════════════════");
Console.WriteLine("  UseStructuredContent IMPACT");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine(@"
When UseStructuredContent = true, the MCP SDK sends BOTH:
  - Content: JSON text (same as today)
  - StructuredContent: raw JSON object (parsed by capable clients)

This means the SHAPE decision is orthogonal to UseStructuredContent.
Whether we pick Format A, B, or C, it gets serialized the same way.
The only difference is that structured-content-capable clients can
parse it without a JSON text round-trip.

Verdict: UseStructuredContent does NOT change the shape decision.
It's a transport optimization, not a format choice.
");

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("  PROTOTYPE COMPLETE");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
