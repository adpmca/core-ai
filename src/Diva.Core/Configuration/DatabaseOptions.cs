namespace Diva.Core.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Provider { get; set; } = "SQLite";    // "SQLite" | "SqlServer"
    public SQLiteOptions SQLite { get; set; } = new();
    public SqlServerOptions SqlServer { get; set; } = new();
}

public sealed class SQLiteOptions
{
    public string ConnectionString { get; set; } = "Data Source=diva.db";
}

public sealed class SqlServerOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public bool UseRls { get; set; } = true;
    public bool UseConnectionPerTenant { get; set; } = false;
}
