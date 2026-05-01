# Phase 23: MCP Server Framework + FileSystem MCP Server

> **Status:** `[x]` Done
> **Depends on:** Phase 5 (MCP client infra), Phase 3 (TenantContextMiddleware)
> **Projects:** `Diva.Tools` (framework + server), `tools/DivaFsMcpServer` (standalone host)
> **Architecture:** Embedded at `/mcp/diva` (Diva.Host) or standalone exe (stdio/HTTP/Windows Service)

## Goal

Reusable MCP server development framework inside `Diva.Tools` plus a FileSystem MCP server that
exposes 12 tools (text, PDF, image) with per-tool enable/disable, path guards, and configurable
quality analysis. Supports two hosting modes: embedded in Diva.Host or standalone Windows exe/service.

## Deliverables

- [x] `McpServerContext`, `McpServerRegistration` (framework scaffolding)
- [x] Abstractions: `IFileSystemPathGuard`, `IToolFilter`, `IPdfReader`, `IImageReader`
- [x] `FileSystemOptions` + `FileSystemOptionsValidator` (`IValidateOptions`)
- [x] `FileSystemPathGuard` (path canonicalisation, deny-list glob, symlink guard)
- [x] `ToolFilter` (enabled-tools list, case-insensitive)
- [x] `PdfReader` (`PdfPig` — pure .NET, cross-platform)
- [x] `ImageReader` (`SixLabors.ImageSharp` — Laplacian blur + exposure histogram + EXIF)
- [x] `FileSystemMcpTools` (12 tools via DIP — thin MCP facade over interfaces)
- [x] Models: `DirectoryEntry`, `FileInfoResult`, `ImageInfoResult`
- [x] Embedded mode: `/mcp/diva` endpoint with `RequireAuthorization`
- [x] Standalone exe: stdio + `--http` modes, `StandaloneApiKeyMiddleware`, Windows Service
- [x] Tests: 15 path guard, 6 tool filter, 12 MCP tools, 6 PDF, 10 image = **61 total**
- [x] `docs/agents.md` MCP server section
- [x] `docs/changelog.md` Phase 23 entry

## File Index

```
src/Diva.Tools/
├── Core/
│   ├── McpServerContext.cs           [NEW]
│   └── McpServerRegistration.cs      [NEW]
└── FileSystem/
    ├── FileSystemOptions.cs          [NEW]
    ├── FileSystemOptionsValidator.cs [NEW]
    ├── FileSystemPathGuard.cs        [NEW]
    ├── ToolFilter.cs                 [NEW]
    ├── Abstractions/
    │   ├── IFileSystemPathGuard.cs   [NEW]
    │   ├── IToolFilter.cs            [NEW]
    │   ├── IPdfReader.cs             [NEW]
    │   └── IImageReader.cs           [NEW]
    ├── Models/
    │   ├── DirectoryEntry.cs         [NEW]
    │   ├── FileInfoResult.cs         [NEW]
    │   └── ImageInfoResult.cs        [NEW]
    ├── Readers/
    │   ├── PdfReader.cs              [NEW]
    │   └── ImageReader.cs            [NEW]
    └── FileSystemMcpTools.cs         [NEW]

tools/DivaFsMcpServer/
├── DivaFsMcpServer.csproj            [NEW]
├── Program.cs                        [NEW]
├── StandaloneApiKeyMiddleware.cs     [NEW]
└── appsettings.json                  [NEW]

tests/Diva.Tools.Tests/
├── TestData/
│   ├── sample.pdf                    [NEW — embedded resource]
│   ├── sample-sharp.jpg              [NEW — embedded resource]
│   └── sample-blurry.jpg             [NEW — embedded resource]
├── FileSystem/
│   ├── FileSystemPathGuardTests.cs   [NEW]
│   ├── ToolFilterTests.cs            [NEW]
│   ├── FileSystemMcpToolsTests.cs    [NEW]
│   ├── PdfReaderTests.cs             [NEW]
│   └── ImageReaderTests.cs           [NEW]
└── Helpers/
    └── McpToolsTestFixtures.cs       [NEW]
```

Modified files:
- `src/Diva.Tools/Diva.Tools.csproj` — add PdfPig + ImageSharp
- `src/Diva.Host/Program.cs` — MCP server registration + endpoint
- `src/Diva.Host/appsettings.json` — add `FileSystem` section
- `src/Diva.Host/Diva.Host.csproj` — add WindowsServices package
- `tests/Diva.Tools.Tests/Diva.Tools.Tests.csproj` — add NSubstitute + packages
- `Diva.slnx` — add standalone project
- `docs/agents.md` — add MCP server section
- `docs/changelog.md` — add Phase 23 entry

## SOLID Design

| Principle | How satisfied |
|-----------|---------------|
| **S** | `FileSystemPathGuard` = path validation only. `ToolFilter` = availability only. `PdfReader` = PDF extraction only. `ImageReader` = image analysis only. `FileSystemMcpTools` = thin MCP facade, delegates everything. |
| **O** | New file readers: implement `IFileReader`-like interface, register in DI — no changes to `FileSystemMcpTools`. New MCP servers: implement `IDivaMcpToolType` — no changes to host registration. |
| **L** | All readers and guards have interfaces; test mocks and prod implementations are substitutable. |
| **I** | `IFileSystemPathGuard` has only path methods. `IToolFilter` has only `IsEnabled`. `IPdfReader` has only PDF methods. `IImageReader` has only image methods. |
| **D** | `FileSystemMcpTools` depends only on interfaces injected at startup. |

## Windows Hosting Options

| Option | Command |
|--------|---------|
| Direct exe | `diva-fs-mcp.exe --http --urls http://localhost:8811` |
| Windows Service | `New-Service -Name DivaFsMcpServer -BinaryPathName "diva-fs-mcp.exe --http"` |
| IIS | `web.config` + `hostingModel="inprocess"` |
| Stdio subprocess | Claude Desktop `claude_desktop_config.json` |
| Embedded (Mode A) | `POST http://localhost:5062/mcp/diva` — requires JWT/API key |
| Docker | `docker run -v C:\data:/data diva-fs-mcp --http` |

## Phase 23.1 — Standalone JWT Authentication (complete)

Added on top of Phase 23. Upgrades `DivaFsMcpServer` with proper JWT client-credentials auth.

| File | Description |
|------|-------------|
| `tools/DivaFsMcpServer/Auth/StandaloneJwtOptions.cs` | Config: SigningKey, Issuer, Audience, TokenExpiryMinutes |
| `tools/DivaFsMcpServer/Auth/StandaloneTokenService.cs` | HMAC-SHA256 JWT issuance + validation |
| `tools/DivaFsMcpServer/StandaloneAuthMiddleware.cs` | Accepts JWT or static key; replaces StandaloneApiKeyMiddleware |
| `tests/DivaFsMcpServer.Tests/` | 14 tests (8 unit, 5 integration + 1 endpoint) |

**Auth decision matrix:**

| Scenario | Accepted credentials |
|---|---|
| `Jwt:SigningKey` set, `StandaloneApiKey` set | JWT (preferred) OR static key |
| `Jwt:SigningKey` empty, `StandaloneApiKey` set | Static key only |
| Both empty | No auth (trusted network) |

Diva platform agents continue using `CredentialRef` with static key — no changes to `McpConnectionManager`.

## Phase 24 Preview

`ClassificationEnabled = false` placeholder in `ImageOptions`. When Phase 24 is built:
- `classify_image` tool added to `FileSystemMcpTools`
- Calls Anthropic vision API (`claude-haiku-4-5-20251001`)
- Returns `{categories, quality, issues, confidence}`
