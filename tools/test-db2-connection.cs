#:package Net.IBM.Data.Db2@9.0.0.400
using IBM.Data.Db2;

var cs = "Server=localhost:50000;Database=contract;UID=db2inst1;Password=SharpDb!Contract2026";
Console.WriteLine($"Connection string: {cs}");
try
{
    using var conn = new DB2Connection(cs);
    await conn.OpenAsync();
    Console.WriteLine($"Connected! State: {conn.State}");
    
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT CURRENT SCHEMA FROM SYSIBM.SYSDUMMY1";
    var result = await cmd.ExecuteScalarAsync();
    Console.WriteLine($"Current schema: {result}");
}
catch (Exception ex)
{
    Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
}
