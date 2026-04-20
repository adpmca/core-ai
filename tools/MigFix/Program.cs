using Microsoft.Data.Sqlite;

var dbPath = @"src/Diva.Host/diva-dev.db";
if (!File.Exists(dbPath)) { Console.WriteLine($"Not found: {dbPath}"); return; }

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

// List current migrations
Console.WriteLine("Current migrations:");
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId";
    using var r = cmd.ExecuteReader();
    while (r.Read()) Console.WriteLine($"  {r.GetString(0)}");
}

// Rename old ID -> new ID
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "UPDATE __EFMigrationsHistory SET MigrationId = '20260412000000_AgentDelegation' WHERE MigrationId = '20260411003535_AgentDelegation'";
    var rows = cmd.ExecuteNonQuery();
    Console.WriteLine($"\nRenamed old->new: {rows} row(s)");
}

// If column exists but no migration record, insert it
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260412000000_AgentDelegation'";
    var exists = (long)cmd.ExecuteScalar()! > 0;
    
    cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('AgentDefinitions') WHERE name='DelegateAgentIdsJson'";
    var colExists = (long)cmd.ExecuteScalar()! > 0;
    Console.WriteLine($"Column exists: {colExists}, Migration recorded: {exists}");

    if (!exists && colExists)
    {
        cmd.CommandText = "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260412000000_AgentDelegation', '10.0.5')";
        cmd.ExecuteNonQuery();
        Console.WriteLine("Inserted migration record");
    }
}

// Clear lock - ensure the expected row exists in released state
using (var cmd = conn.CreateCommand())
{
    // List all tables
    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
    Console.WriteLine("\nAll tables:");
    using var tableReader = cmd.ExecuteReader();
    while (tableReader.Read()) Console.WriteLine($"  {tableReader.GetString(0)}");
}

using (var cmd = conn.CreateCommand())
{
    // Show all columns in AgentDefinitions
    cmd.CommandText = "SELECT name FROM pragma_table_info('AgentDefinitions') ORDER BY cid";
    Console.WriteLine("\nAgentDefinitions columns:");
    using var colReader = cmd.ExecuteReader();
    while (colReader.Read()) Console.WriteLine($"  {colReader.GetString(0)}");
}

// Check which tables have a 'Name' column
foreach (var tbl in new[] { "AgentDefinitions", "McpCredentials", "TenantBusinessRules",
    "RulePacks", "RulePackItems", "AgentSessions", "TenantLlmConfigs", "GroupLlmConfigs" })
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tbl}') WHERE name='Name'";
    try
    {
        var hasName = (long)cmd.ExecuteScalar()! > 0;
        if (hasName) Console.WriteLine($"\n{tbl} has 'Name' column");
        else Console.WriteLine($"\n{tbl} does NOT have 'Name' column");
    }
    catch { Console.WriteLine($"\n{tbl} table does not exist"); }
}

// ── Fix missing columns from partially-applied 20260326195152_AddLlmConfigCatalog ─────

// TenantLlmConfigs: Name column
FixMissingColumn(conn, "TenantLlmConfigs", "Name", "TEXT", null);

// PlatformLlmConfigs: Name column
FixMissingColumn(conn, "PlatformLlmConfigs", "Name", "TEXT", "'Default'");

// GroupLlmConfigs: PlatformConfigRef column
FixMissingColumn(conn, "GroupLlmConfigs", "PlatformConfigRef", "INTEGER", null);

// Also check AvailableModelsJson and DeploymentName in all LLM config tables
foreach (var table in new[] { "PlatformLlmConfigs", "TenantLlmConfigs", "GroupLlmConfigs" })
{
    FixMissingColumn(conn, table, "AvailableModelsJson", "TEXT", null);
    FixMissingColumn(conn, table, "DeploymentName", "TEXT", null);
}

// LlmConfigId column already exists in AgentDefinitions
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('AgentDefinitions') WHERE name='LlmConfigId'";
    var hasCol = (long)cmd.ExecuteScalar()! > 0;
    if (!hasCol)
    {
        cmd.CommandText = "ALTER TABLE AgentDefinitions ADD COLUMN LlmConfigId INTEGER NULL";
        cmd.ExecuteNonQuery();
        Console.WriteLine("\nAdded missing LlmConfigId column to AgentDefinitions");
    }
    else
    {
        Console.WriteLine("\nLlmConfigId column already exists in AgentDefinitions");
    }
}

// Fix missing LlmConfigId column in GroupAgentTemplates
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('GroupAgentTemplates') WHERE name='LlmConfigId'";
    var hasCol = (long)cmd.ExecuteScalar()! > 0;
    if (!hasCol)
    {
        cmd.CommandText = "ALTER TABLE GroupAgentTemplates ADD COLUMN LlmConfigId INTEGER NULL";
        cmd.ExecuteNonQuery();
        Console.WriteLine("Added missing LlmConfigId column to GroupAgentTemplates");
    }
    else
    {
        Console.WriteLine("LlmConfigId column already exists in GroupAgentTemplates");
    }
}

using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='__EFMigrationsLock'";
    var lockTableExists = cmd.ExecuteScalar() != null;
    Console.WriteLine($"\nLock table exists: {lockTableExists}");
    
    if (lockTableExists)
    {
        cmd.CommandText = "SELECT * FROM __EFMigrationsLock";
        using var r2 = cmd.ExecuteReader();
        var hasRows = false;
        while (r2.Read())
        {
            hasRows = true;
            for (int i = 0; i < r2.FieldCount; i++)
                Console.Write($"  {r2.GetName(i)}={r2.GetValue(i)}");
            Console.WriteLine();
        }
        if (!hasRows) Console.WriteLine("  (empty)");
    }
}
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "DELETE FROM __EFMigrationsLock";
    var deleted = cmd.ExecuteNonQuery();
    Console.WriteLine($"Lock rows deleted: {deleted}");
    
    // Pre-insert with Acquired=false so MigrateAsync can UPDATE it
    // EF Core 10 expects: UPDATE __EFMigrationsLock SET Acquired = 1 WHERE Id = 1 AND Acquired = 0
    // If zero rows, it actually tries to INSERT first in some versions
    // Let's ensure 0 rows so EF inserts fresh
}

Console.WriteLine("Done!");

// Fix corrupted DelegateAgentIdsJson values (legacy: [null], [NaN], etc.)
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "UPDATE AgentDefinitions SET DelegateAgentIdsJson = NULL WHERE DelegateAgentIdsJson IN ('[null]', '[NaN]', '[]', '[undefined]')";
    var cleaned = cmd.ExecuteNonQuery();
    if (cleaned > 0)
        Console.WriteLine($"  Cleaned {cleaned} corrupted DelegateAgentIdsJson value(s)");
}

static void FixMissingColumn(SqliteConnection conn, string table, string column, string type, string? defaultValue)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'";
    var hasCol = (long)cmd.ExecuteScalar()! > 0;
    if (!hasCol)
    {
        var def = defaultValue is not null ? $" DEFAULT {defaultValue}" : " NULL";
        cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}{def}";
        cmd.ExecuteNonQuery();
        Console.WriteLine($"  Added missing {column} column to {table}");
    }
    else
    {
        Console.WriteLine($"  {column} already exists in {table}");
    }
}
