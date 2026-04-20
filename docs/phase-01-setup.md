# Phase 1: Solution Scaffold & Configuration

> **Status:** `[ ]` Not Started
> **Depends on:** nothing
> **Blocks:** all other phases

---

## Goal

Create the .NET 8 solution with all projects, NuGet packages, Docker setup, and base configuration files. No business logic yet — just the skeleton that compiles and runs.

---

## Files to Create

### Solution & Projects
```
Diva.sln
src/Diva.Core/Diva.Core.csproj
src/Diva.Infrastructure/Diva.Infrastructure.csproj
src/Diva.Agents/Diva.Agents.csproj
src/Diva.Tools/Diva.Tools.csproj
src/Diva.TenantAdmin/Diva.TenantAdmin.csproj
src/Diva.Host/Diva.Host.csproj
tests/Diva.Agents.Tests/Diva.Agents.Tests.csproj
tests/Diva.Tools.Tests/Diva.Tools.Tests.csproj
tests/Diva.TenantAdmin.Tests/Diva.TenantAdmin.Tests.csproj
```

### Configuration
```
src/Diva.Host/appsettings.json
src/Diva.Host/appsettings.Development.json
```

### Docker
```
Dockerfile
docker-compose.yml               (SQLite default)
docker-compose.enterprise.yml    (SQL Server + LiteLLM)
.dockerignore
```

### Prompt templates (empty stubs)
```
prompts/supervisor/orchestrator.v1.txt
prompts/analytics/planner.v2.txt
prompts/analytics/text-to-sql.v1.txt
prompts/shared/security-constraints.v1.txt
prompts/shared/output-format.v1.txt
```

---

## NuGet Packages

### Diva.Core
```xml
<!-- No external dependencies — pure models/interfaces -->
```

### Diva.Infrastructure
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="9.0.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.*" />
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="9.0.*" />
<PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="9.0.*" />
<PackageReference Include="Serilog.AspNetCore" Version="8.*" />
<PackageReference Include="Serilog.Sinks.Seq" Version="*" />
```

### Diva.Agents
```xml
<PackageReference Include="Microsoft.SemanticKernel" Version="1.*" />
<PackageReference Include="Microsoft.SemanticKernel.Agents.Core" Version="1.*" />
<PackageReference Include="Microsoft.AutoGen.Core" Version="0.14.*" />
<PackageReference Include="Microsoft.AutoGen.Agents" Version="0.14.*" />
```

### Diva.Tools
```xml
<PackageReference Include="ModelContextProtocol" Version="0.1.*" />
<PackageReference Include="ModelContextProtocol.AspNetCore" Version="0.1.*" />
```

### Diva.TenantAdmin
```xml
<!-- References Diva.Infrastructure, Diva.Core -->
<PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="9.0.*" />
```

### Diva.Host
```xml
<PackageReference Include="Microsoft.AspNetCore.SignalR" Version="*" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="*" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="*" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="*" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="*" />
<PackageReference Include="prometheus-net.AspNetCore" Version="*" />
<PackageReference Include="AspNetCore.HealthChecks.SqlServer" Version="*" />
<PackageReference Include="AspNetCore.HealthChecks.Uris" Version="*" />
```

---

## appsettings.json (full template)

```json
{
  "LLM": {
    "UseLiteLLM": false,
    "DirectProvider": {
      "Provider": "Anthropic",
      "ApiKey": "${ANTHROPIC_API_KEY}",
      "Model": "claude-sonnet-4-20250514"
    },
    "LiteLLM": {
      "BaseUrl": "http://litellm:4000",
      "MasterKey": "${LITELLM_MASTER_KEY}",
      "DefaultModel": "claude-sonnet"
    }
  },
  "OAuth": {
    "Authority": "https://your-identity-provider.com",
    "Audience": "diva-api",
    "ValidateIssuer": true,
    "ValidateAudience": true,
    "ClaimMappings": {
      "TenantId": "tenant_id",
      "TenantName": "tenant_name",
      "UserId": "sub",
      "SiteIds": "site_ids",
      "Roles": "roles",
      "AgentAccess": "agent_access"
    },
    "PropagateToken": true
  },
  "Database": {
    "Provider": "SQLite",
    "SQLite": {
      "ConnectionString": "Data Source=diva.db"
    },
    "SqlServer": {
      "ConnectionString": "",
      "UseRls": true,
      "UseConnectionPerTenant": false
    }
  },
  "A2A": {
    "Enabled": true,
    "ServerPort": 8080,
    "AgentCardsPath": "./AgentCards"
  },
  "MCP": {
    "Enabled": true,
    "Servers": [
      { "Name": "analytics", "Transport": "stdio" },
      { "Name": "Reservation", "Transport": "stdio" }
    ]
  },
  "Memory": {
    "WorkingMemoryLimit": 2000,
    "ShortTermMemoryLimit": 4000,
    "ReservedTokens": 1000,
    "VectorStore": {
      "Provider": "InMemory"
    },
    "AgentOverrides": {
      "analytics-agent": { "WorkingMemoryLimit": 3000 },
      "supervisor-agent": { "WorkingMemoryLimit": 4000 }
    }
  },
  "Agents": {
    "MaxIterations": 10,
    "DefaultTemperature": 0.7
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

---

## Project References (csproj dependency graph)

```
Diva.Core               (no project refs)
Diva.Infrastructure     → Diva.Core
Diva.TenantAdmin        → Diva.Core, Diva.Infrastructure
Diva.Tools              → Diva.Core, Diva.Infrastructure
Diva.Agents             → Diva.Core, Diva.Infrastructure, Diva.TenantAdmin, Diva.Tools
Diva.Host               → all above
```

---

## CLI Commands to Bootstrap

```bash
# Create solution
dotnet new sln -n Diva

# Create projects
dotnet new classlib -n Diva.Core         -o src/Diva.Core        --framework net8.0
dotnet new classlib -n Diva.Infrastructure -o src/Diva.Infrastructure --framework net8.0
dotnet new classlib -n Diva.TenantAdmin  -o src/Diva.TenantAdmin  --framework net8.0
dotnet new classlib -n Diva.Tools        -o src/Diva.Tools        --framework net8.0
dotnet new classlib -n Diva.Agents       -o src/Diva.Agents       --framework net8.0
dotnet new webapi   -n Diva.Host         -o src/Diva.Host         --framework net8.0

# Test projects
dotnet new xunit -n Diva.Agents.Tests    -o tests/Diva.Agents.Tests
dotnet new xunit -n Diva.Tools.Tests     -o tests/Diva.Tools.Tests
dotnet new xunit -n Diva.TenantAdmin.Tests -o tests/Diva.TenantAdmin.Tests

# Add all to solution
dotnet sln add src/**/*.csproj tests/**/*.csproj

# Verify build
dotnet build
```

---

## Verification

- [ ] `dotnet build` succeeds with 0 errors
- [ ] `dotnet run --project src/Diva.Host` starts on port 8080
- [ ] `GET http://localhost:8080/health/live` returns 200
- [ ] Docker: `docker-compose up` starts successfully (SQLite default)
