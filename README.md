# Diva AI

[![Build](https://github.com/adpmca/diva-ai/actions/workflows/ci.yml/badge.svg)](https://github.com/adpmca/diva-ai/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)

Open-source multi-tenant AI agent platform — self-hostable, white-labelable, built on .NET 10 + Semantic Kernel.

---

## Features

- **Multi-tenant** — full tenant isolation via EF query filters and JWT claims
- **ReAct agent loop** — streaming SSE, tool call trace, plan detection, continuation windows
- **MCP tool servers** — multi-server support, Docker MCP Gateway, credential vault
- **Rule learning** — agents extract and propose business rules from conversations
- **Rule Packs** — DB-driven configurable hook rule bundles (9 rule types)
- **A2A protocol** — agent-to-agent delegation, remote agent discovery
- **Custom agents** — pluggable archetypes, lifecycle hooks, `BaseCustomAgent`
- **Response verification** — Off / ToolGrounded / LlmVerifier / Strict / Auto modes
- **Embeddable widget** — drop-in `<script>` tag chat widget with SSE streaming
- **White-labelable** — swap product name, slug, JWT audience via env vars; full namespace rebrand script included
- **Admin portal** — React + Vite + shadcn/ui management UI

---

## Quick Start

```bash
# 1. Copy and configure environment
cp .env.example .env
cp src/Diva.Host/appsettings.Development.example.json src/Diva.Host/appsettings.Development.json
# Edit .env: set ANTHROPIC_API_KEY and Credentials__MasterKey

# 2. Start with Docker Compose
docker compose up -d

# 3. Open admin portal
open http://localhost:8080
```

---

## Architecture

```
src/
  Diva.Core/            # Models, DTOs, config interfaces (no dependencies)
  Diva.Infrastructure/  # DB (EF Core + SQLite/SQL Server), Auth, Sessions, Learning
  Diva.Agents/          # SK ChatCompletionAgent wrappers, Supervisor pipeline
  Diva.Tools/           # MCP tool infrastructure
  Diva.TenantAdmin/     # Business rules service, tenant-aware prompt builder
  Diva.Host/            # ASP.NET Core 10 entry point, controllers, SignalR hub
admin-portal/           # React + Vite + TypeScript admin UI
```

**Dependency order:** Core → Infrastructure → Tools → TenantAdmin → Agents → Host

---

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `ANTHROPIC_API_KEY` | — | Required for Anthropic provider |
| `Credentials__MasterKey` | — | Base64-encoded 32-byte AES-256 key for MCP credential encryption. Must be stable — changing it invalidates all stored credentials. Generate: `openssl rand -base64 32` |
| `LocalAuth__SigningKey` | — | JWT signing key (min 32 chars). Changing it invalidates all active sessions. |
| `AppBranding__ProductName` | `Diva AI` | Display name shown in the UI |
| `AppBranding__Slug` | `diva` | Lowercase slug — localStorage key prefix. No spaces. Changing after go-live invalidates browser sessions. |
| `AppBranding__ApiAudience` | `diva-api` | JWT audience string. Changing it invalidates existing JWTs. |
| `AppBranding__LocalIssuer` | `diva-local` | JWT local issuer. Changing it invalidates existing JWTs. |
| `Database__Provider` | `SQLite` | `SQLite` or `SqlServer` |
| `Database__SQLite__ConnectionString` | `Data Source=diva.db` | SQLite path |
| `OAuth__Authority` | — | OIDC authority URL for SSO |
| `AdminPortal__CorsOrigin` | `http://localhost:5173` | Admin portal origin for CORS |

See `.env.example` for the full list.

---

## White-Labeling

Diva AI supports two white-labeling paths — no fork required for most use cases.

| Need | Path |
|------|------|
| Change product name in UI | Set `AppBranding__ProductName` + `VITE_APP_NAME` env vars |
| Change JWT issuer/audience | Set `AppBranding__LocalIssuer` + `AppBranding__ApiAudience` |
| Full C# namespace rename (`AcmeCorp.*`) | Run `./tools/rebrand.ps1 -NewName AcmeCorp -NewSlug acme` |

See [docs/white-labeling.md](docs/white-labeling.md) for the full guide.

---

## Dev Setup

**API:**
```bash
cp src/Diva.Host/appsettings.Development.example.json src/Diva.Host/appsettings.Development.json
# Edit appsettings.Development.json with your API key
dotnet run --project src/Diva.Host
```

**Admin portal:**
```bash
cd admin-portal
cp .env.example .env
npm install
npm run dev   # http://localhost:5173

# No API needed — use MSW sandbox:
VITE_MOCK=true npm run dev
```

---

## Running Tests

```bash
dotnet test Diva.slnx
```

Individual test projects:
```bash
dotnet test tests/Diva.Agents.Tests
dotnet test tests/Diva.TenantAdmin.Tests
dotnet test tests/Diva.Tools.Tests
```

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

---

## Security

Found a vulnerability? See [SECURITY.md](SECURITY.md).

---

## License

Apache 2.0 — see [LICENSE](LICENSE).
