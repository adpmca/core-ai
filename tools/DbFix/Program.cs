using Microsoft.Data.Sqlite;

var baseDir = args.Length > 0 ? args[0] : "src/Diva.Host";
var dbs = new[] { Path.Combine(baseDir, "diva.db"), Path.Combine(baseDir, "diva-dev.db") };

foreach (var db in dbs)
{
    if (!File.Exists(db)) { Console.WriteLine($"{db}: not found"); continue; }
    Console.WriteLine($"\n=== {Path.GetFileName(db)} ===");
    using var conn = new SqliteConnection($"Data Source={db}");
    conn.Open();

    bool ColExists(string table, string col)
    {
        using var c = conn.CreateCommand();
        c.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{col}'";
        return (long)c.ExecuteScalar()! > 0;
    }

    bool IdxExists(string idx)
    {
        using var c = conn.CreateCommand();
        c.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='{idx}'";
        return (long)c.ExecuteScalar()! > 0;
    }

    void Exec(string sql)
    {
        using var c = conn.CreateCommand();
        c.CommandText = sql;
        c.ExecuteNonQuery();
        Console.WriteLine($"  OK: {sql}");
    }

    // ── Clear stale migration lock ────────────────────────────────────────────
    using (var c = conn.CreateCommand())
    {
        c.CommandText = "DELETE FROM __EFMigrationsLock WHERE Id = 1";
        Console.WriteLine($"  Cleared {c.ExecuteNonQuery()} lock row(s)");
    }

    // ── Reverse any partial AddLlmConfigCatalog columns ──────────────────────
    // SQLite DROP COLUMN requires 3.35.0+ which is bundled here
    if (ColExists("GroupAgentTemplates", "LlmConfigId"))
        Exec("ALTER TABLE GroupAgentTemplates DROP COLUMN LlmConfigId");

    if (ColExists("AgentDefinitions", "LlmConfigId"))
        Exec("ALTER TABLE AgentDefinitions DROP COLUMN LlmConfigId");

    if (ColExists("TenantLlmConfigs", "Name"))
    {
        if (IdxExists("IX_TenantLlmConfigs_TenantId_Name"))
            Exec("DROP INDEX \"IX_TenantLlmConfigs_TenantId_Name\"");
        Exec("ALTER TABLE TenantLlmConfigs DROP COLUMN Name");
        // Restore the original unique index on TenantId alone
        if (!IdxExists("IX_TenantLlmConfigs_TenantId"))
            Exec("CREATE UNIQUE INDEX \"IX_TenantLlmConfigs_TenantId\" ON TenantLlmConfigs(TenantId)");
    }

    if (ColExists("PlatformLlmConfigs", "Name"))
    {
        if (IdxExists("IX_PlatformLlmConfigs_Name"))
            Exec("DROP INDEX \"IX_PlatformLlmConfigs_Name\"");
        Exec("ALTER TABLE PlatformLlmConfigs DROP COLUMN Name");
    }

    if (ColExists("GroupLlmConfigs", "PlatformConfigRef"))
    {
        if (IdxExists("IX_GroupLlmConfigs_PlatformConfigRef"))
            Exec("DROP INDEX \"IX_GroupLlmConfigs_PlatformConfigRef\"");
        Exec("ALTER TABLE GroupLlmConfigs DROP COLUMN PlatformConfigRef");
    }

    // ── Report final state ────────────────────────────────────────────────────
    Console.WriteLine("  Final column state:");
    foreach (var (table, col) in new[] {
        ("GroupAgentTemplates","LlmConfigId"), ("AgentDefinitions","LlmConfigId"),
        ("TenantLlmConfigs","Name"), ("PlatformLlmConfigs","Name"),
        ("GroupLlmConfigs","PlatformConfigRef") })
    {
        Console.WriteLine($"    {table}.{col}: {(ColExists(table, col) ? "EXISTS" : "clean")}");
    }

    // ── Fix AgentDelegation migration ID (timestamp was corrected from 20260411 to 20260412) ──
    using (var c = conn.CreateCommand())
    {
        c.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260411003535_AgentDelegation'";
        var oldExists = (long)c.ExecuteScalar()! > 0;
        if (oldExists)
        {
            Exec("UPDATE __EFMigrationsHistory SET MigrationId = '20260412000000_AgentDelegation' WHERE MigrationId = '20260411003535_AgentDelegation'");
        }
        else
        {
            // Column exists but no migration record — insert it
            c.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260412000000_AgentDelegation'";
            var newExists = (long)c.ExecuteScalar()! > 0;
            if (!newExists && ColExists("AgentDefinitions", "DelegateAgentIdsJson"))
            {
                Exec("INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260412000000_AgentDelegation', '10.0.5')");
            }
        }
    }
}

Console.WriteLine("\nDone. Both DBs are ready for migration.");
