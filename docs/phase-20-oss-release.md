# Phase 20 — Open-Source Release + White-Label Strategy

## Goal

Publish Diva AI as an open-source project on GitHub. Allow customer organisations to deploy under
their own brand/namespace without forking. Maintain premium features privately via an Open Core model.

---

## Phase 0 — Pre-Release Security (BLOCKING)

> Must complete before any `git push` to a public repository.

### 0.1 Rotate the exposed Anthropic API key

`src/Diva.Host/appsettings.Development.json` contains a live key. Go to console.anthropic.com →
revoke it → generate a replacement → store in local `.env` only.

### 0.2 Rewrite git history

```bash
pip install git-filter-repo
git tag pre-oss-backup   # local safety tag — do NOT push

# replacements.txt:
# sk-ant-api03-<key>==>REDACTED_ROTATE_BEFORE_USE

git filter-repo --replace-text replacements.txt
# All collaborators must re-clone after this.
```

### 0.3 Sanitise `appsettings.Development.json`

- Rename to `appsettings.Development.example.json` (all placeholder values, tracked)
- Add `appsettings.Development.json` to `.gitignore`
- Replace API key value with `"YOUR_ANTHROPIC_API_KEY_HERE"`

### 0.4 Clear the hardcoded AES MasterKey

`src/Diva.Host/appsettings.json` → `"Credentials": { "MasterKey": "" }`.
Document that production deployments **must** set `Credentials__MasterKey` via environment variable.
Rotate the key in any live deployment; re-save all MCP credentials afterwards.

### 0.5 Update `.gitignore`

```gitignore
# Config overrides with secrets
appsettings.Development.json
!appsettings.Development.example.json
```

---

## Phase 1 — License

**Apache 2.0** — explicit patent grants, OSI-approved, no copyleft, enterprise-compatible.

Create `LICENSE` at repo root.
Copyright line: `Copyright 2024–2026 [Your Name or Organisation]`

---

## Phase 2 — GitHub Repository Setup

**Repository settings:**
- Visibility: Public
- Description: `Open-source multi-tenant AI agent platform — self-hostable, white-labelable`
- Topics: `ai-agents`, `dotnet`, `react`, `multi-tenant`, `llm`, `anthropic`, `self-hosted`, `mcp`, `agent-platform`
- Enable: Issues, Discussions, Projects. Disable: Wiki.

**Branch protection on `main`:**
- Require PR + 1 approval before merge (waivable for solo maintainer)
- Require `ci.yml` to pass
- Disallow force-push and branch deletion

**Security:**
- Enable GitHub private vulnerability reporting
- Enable Dependabot (NuGet `/src`, npm `/admin-portal`, GitHub Actions `/`)
- Enable secret scanning

---

## Phase 3 — Community / OSS Files

### `README.md` (create at root)

1. Title + badge row (build, license, stars)
2. One-paragraph description
3. Admin portal screenshot
4. Quick Start (3 Docker commands)
5. Architecture overview + C# project tree
6. Environment variable reference table
7. White-labeling section + link to `docs/white-labeling.md`
8. Dev setup (dotnet run + npm run dev)
9. Running tests
10. Contributing link
11. License

### `CONTRIBUTING.md`
Bug reporting, feature proposals, dev setup, coding standards (XML doc on public API, tests required),
PR process (1 approval, CI pass, changelog entry), Conventional Commits.

### `SECURITY.md`
Supported versions (main/latest), report via GitHub private reporting or `security@[domain]`,
72 h acknowledge / 7 d triage SLA. Notes on `Credentials__MasterKey` and `LocalAuth__SigningKey`.

### `CODE_OF_CONDUCT.md`
Contributor Covenant 2.1 verbatim.

### `.github/ISSUE_TEMPLATE/bug_report.yml`
Fields: description, repro steps, expected vs actual, environment, logs.

### `.github/ISSUE_TEMPLATE/feature_request.yml`
Fields: problem, solution, alternatives.

### `.github/ISSUE_TEMPLATE/config.yml`
`blank_issues_enabled: false`.

### `.github/PULL_REQUEST_TEMPLATE.md`
Checklist: tests, `appsettings.Development.example.json`, `.env.example`, changelog entry, no secrets.

### `.github/dependabot.yml`
```yaml
version: 2
updates:
  - package-ecosystem: nuget
    directory: /src
    schedule: { interval: weekly }
  - package-ecosystem: npm
    directory: /admin-portal
    schedule: { interval: weekly }
  - package-ecosystem: github-actions
    directory: /
    schedule: { interval: weekly }
```

---

## Phase 4 — Configuration-Driven Branding

Customers change the product name via env vars — no fork, no code change, stays on upstream.

### New file: `src/Diva.Core/Configuration/AppBrandingOptions.cs`

```csharp
namespace Diva.Core.Configuration;

public sealed class AppBrandingOptions
{
    public const string SectionName = "AppBranding";

    /// <summary>Display name shown in the UI. Default: "Diva AI"</summary>
    public string ProductName { get; set; } = "Diva AI";

    /// <summary>Lowercase slug — localStorage key prefix, JWT defaults. No spaces.</summary>
    public string Slug { get; set; } = "diva";

    /// <summary>OAuth audience string for JWT validation. Default: "diva-api"</summary>
    public string ApiAudience { get; set; } = "diva-api";

    /// <summary>JWT local issuer. Default: "diva-local"</summary>
    public string LocalIssuer { get; set; } = "diva-local";
}
```

Register in `src/Diva.Host/Program.cs`:
```csharp
builder.Services.Configure<AppBrandingOptions>(
    builder.Configuration.GetSection(AppBrandingOptions.SectionName));
```

### `src/Diva.Infrastructure/Auth/LocalAuthService.cs`

Replace hardcoded `"diva-local"` / `"diva-api"` with `IOptions<AppBrandingOptions>` injection.

> Changing these values invalidates existing JWTs — document as a breaking upgrade change.

### New file: `admin-portal/src/lib/brand.ts`

```typescript
export const APP_NAME = import.meta.env.VITE_APP_NAME ?? "Diva AI";
export const APP_SLUG = import.meta.env.VITE_APP_SLUG ?? "diva";

export function storageKey(key: string): string {
  return `${APP_SLUG}_${key}`;
}
```

Files to update:
- `admin-portal/src/components/layout/app-sidebar.tsx` — replace `"Diva AI"` with `APP_NAME`
- `admin-portal/src/components/LoginPage.tsx` — replace `"Diva AI"` with `APP_NAME`
- `admin-portal/index.html` — `<title>%VITE_APP_NAME% Admin</title>`
- All `diva_token`, `diva_tenant_id` etc. localStorage keys → `storageKey("token")`, `storageKey("tenant_id")`

Create `admin-portal/.env.example`:
```
VITE_APP_NAME=Diva AI
VITE_APP_SLUG=diva
VITE_API_URL=http://localhost:5062
VITE_AUTH_ENABLED=false
VITE_TENANT_ID=
VITE_MOCK=false
```

Add to root `.env.example`:
```
AppBranding__ProductName=Diva AI
AppBranding__Slug=diva
AppBranding__ApiAudience=diva-api
AppBranding__LocalIssuer=diva-local
```

---

## Phase 5 — CI/CD Workflows

### `.github/workflows/ci.yml`

Triggers: push to `main`, any PR branch, `workflow_dispatch`.

```yaml
jobs:
  backend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.x' }
      - run: dotnet restore Diva.slnx
      - run: dotnet build Diva.slnx -c Release --no-restore
      - run: dotnet test Diva.slnx --no-build -c Release --logger "trx;LogFileName=results.trx"
      - uses: actions/upload-artifact@v4
        with: { name: test-results, path: '**/results.trx' }

  frontend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: '22' }
      - run: npm ci
        working-directory: admin-portal
      - run: npm run build
        working-directory: admin-portal

  docker-build:
    runs-on: ubuntu-latest
    if: github.event_name == 'push' && github.ref == 'refs/heads/main'
    steps:
      - uses: actions/checkout@v4
      - uses: docker/build-push-action@v5
        with: { context: ., push: false }
```

### `.github/workflows/release.yml`

Trigger: push of tag `v*.*.*`

1. Full CI
2. Build Docker → push to `ghcr.io/[owner]/diva-ai` with `:1.0.0`, `:1.0`, `:latest` tags
3. `gh release create $TAG --generate-notes`
4. Attach quick-start zip: `docker-compose.yml`, `.env.example`, `prompts/`

### `.github/workflows/secret-scan.yml`

TruffleHog diff scan on every `pull_request`. Required status check.

---

## Phase 6 — Rebrand Scripts (full namespace rename)

For customers who need C# namespaces to say `AcmeCorp.*`.

### `tools/rebrand.ps1` (PowerShell 7, cross-platform primary)

Parameters: `-NewName "AcmeCorp"`, `-NewSlug "acme"`, `-NewApiAudience "acme-api"`

Steps:
1. Validate inputs (no spaces in slug, PascalCase name)
2. Git safety — abort if uncommitted changes; create `rebrand/$NewSlug` branch
3. Print change summary, ask for confirmation
4. Find + replace in `.cs`, `.csproj`, `.slnx`, `.md`, `.yml`, `.txt`, `Dockerfile`:
   - `Diva.` → `$NewName.` (namespaces)
   - `"diva-local"` → `"$NewSlug-local"` (JWT issuer)
   - `"diva-api"` → `"$NewApiAudience"` (JWT audience)
   - `"Diva AI"` → `$NewName` (display string)
   - `diva_` → `${NewSlug}_` (localStorage prefix)
   - `diva-api` / `diva-data` in docker-compose → `$NewSlug-api` / `$NewSlug-data`
5. Rename dirs/files (`src/Diva.*` → `src/$NewName.*`, `Diva.slnx`, ENTRYPOINT dll)
6. Update `AppBranding` defaults in appsettings
7. `git commit -am "chore: rebrand Diva → $NewName"`
8. Print build verification commands

Script must be **idempotent** — double-run must not corrupt files.

### `tools/rebrand.sh` (Bash companion for Linux/macOS CI)

Identical logic via `sed -i` + `mv`.

---

## Phase 7 — White-Labeling Documentation: `docs/white-labeling.md`

| Need | Path |
|------|------|
| Change product name in UI | Configuration-driven (no fork) |
| Change Docker service names | Edit `docker-compose.yml` manually |
| Publish NuGet packages under own namespace | Full rebrand script |
| Internal policy: no "Diva" in code | Full rebrand script |
| Pull security patches from upstream | Configuration-driven |

Sections: Configuration-driven (worked example for "AcmeCorp AI"), Full Namespace Rebrand
(step-by-step + build verification), Staying Current with Upstream.

**Caveats to document:**
- Changing `AppBranding__Slug` after go-live invalidates all browser sessions
- Changing `AppBranding__LocalIssuer`/`ApiAudience` invalidates JWTs — users must re-login
- `Credentials__MasterKey` rotation is separate (encrypts DB data)

---

## Phase 8 — Initial Release Process

Tag `v1.0.0` (or `v0.1.0` if API still in flux).

1. Add `## [1.0.0] - 2026-04-XX` section to `docs/changelog.md`
2. `git tag -a v1.0.0 -m "Initial open-source release"`
3. `git push origin v1.0.0`
4. `release.yml` fires automatically

Docker image tags: `ghcr.io/[owner]/diva-ai:1.0.0`, `:1.0`, `:latest`

---

## Phase 9 — Open Core Model (Premium Features in Private Repo)

The OSS repo contains the full platform. A separate **private** repo adds premium features by
registering additional implementations via the existing `IEnumerable<T>` extension points — without
touching OSS code.

### Existing Extension Points

| Interface | File | Premium Use Case |
|-----------|------|-----------------|
| `IAgentLifecycleHook` | `src/Diva.Core/Models/IAgentLifecycleHook.cs` | Audit, compliance, metering |
| `ISupervisorPipelineStage` | `src/Diva.Agents/Supervisor/ISupervisorPipelineStage.cs` | Priority routing, SLA enforcement |
| `ISetupAssistantContextEnricher` | `src/Diva.Core/Models/AgentSetupDtos.cs` | CRM data, internal doc enrichment |
| `BaseCustomAgent` | `src/Diva.Infrastructure/` | Proprietary agent archetypes |
| Rule Pack types (DB-driven) | — | New rule types need no code |

`IAgentLifecycleHook` is discovered via `HookTypeRegistry.BuildFromAssemblies()` — loading the
premium DLL is enough for hooks to self-register.

### Private Repository Structure

```
diva-enterprise/
  src/
    Diva.Enterprise.Extensions/
      Hooks/                         ← IAgentLifecycleHook implementations
      Stages/                        ← ISupervisorPipelineStage implementations
      Agents/                        ← proprietary BaseCustomAgent subclasses
      Billing/                       ← usage tracking, seat limits, feature flags
      ServiceCollectionExtensions.cs ← AddEnterpriseExtensions()
    Diva.Enterprise.Host/            ← thin host referencing OSS + premium
      Program.cs
      appsettings.json
    Diva.Enterprise.Tests/
  docker-compose.enterprise.yml
```

### Registration Pattern

```csharp
// Diva.Enterprise.Extensions/ServiceCollectionExtensions.cs
public static IServiceCollection AddEnterpriseExtensions(
    this IServiceCollection services, IConfiguration config)
{
    services.AddSingleton<IAgentLifecycleHook, EnterpriseAuditHook>();
    services.AddSingleton<ISupervisorPipelineStage, EnterprisePriorityStage>();
    services.AddSingleton<ISetupAssistantContextEnricher, EnterpriseContextEnricher>();
    services.Configure<EnterpriseOptions>(config.GetSection("Enterprise"));
    return services;
}

// Diva.Enterprise.Host/Program.cs
builder.Services.AddDivaPlatform(builder.Configuration);         // OSS — unchanged
builder.Services.AddEnterpriseExtensions(builder.Configuration); // premium — adds on top
```

### Sync Cadence

| Release type | Action |
|---|---|
| Patch (1.0.x) | Sync within 24 h — security fixes |
| Minor (1.x.0) | Test premium compatibility, merge within 1 week |
| Major (x.0.0) | Treat as breaking — audit for API changes first |

Dependency via NuGet (preferred once packages are published) or git submodule at `vendor/diva-oss/`.

### OSS vs Premium Feature Split

| Feature | OSS | Premium |
|---------|:---:|:-------:|
| Core agent execution, ReAct loop, tools, streaming | ✓ | |
| Multi-tenancy, Auth/SSO | ✓ | |
| Rule Packs, custom agents, A2A | ✓ | |
| Basic session tracing | ✓ | |
| Advanced audit trail (SIEM export, tamper-proof log) | | ✓ |
| Seat/usage metering + billing integration | | ✓ |
| Enterprise guardrails (PII redaction, content filter) | | ✓ |
| Priority routing / SLA enforcement pipeline stage | | ✓ |
| Proprietary domain agents (industry-specific) | | ✓ |

---

## Execution Order

| Order | Step | Blocker? |
|-------|------|----------|
| 1 | Phase 0 — Security cleanup | **Yes — before any public push** |
| 2 | Phase 1 — LICENSE | No |
| 3 | Phase 3 — Community files | No |
| 4 | Phase 2 — GitHub settings | After first push |
| 5 | Phase 5 — CI/CD | No |
| 6 | Phase 4 — Config branding | No |
| 7 | Phase 6 — Rebrand scripts | No |
| 8 | Phase 7 — White-labeling docs | No |
| 9 | Phase 8 — Tag v1.0.0 | After all above |
| 10 | Phase 9 — Open Core private repo | After OSS repo is public |

---

## Verification Checklist

1. `git log --all --full-history --oneline -- appsettings.Development.json` — no live key in any commit
2. Push test branch → `ci.yml` passes backend + frontend
3. Set `VITE_APP_NAME=TestCorp` → login page and sidebar show "TestCorp"
4. Set `AppBranding__ProductName=TestCorp AI` → JWT issuer/audience updated
5. Run `./tools/rebrand.ps1 -NewName AcmeCorp -NewSlug acme` → `dotnet build AcmeCorp.slnx` passes
6. `docker compose up -d` → `http://localhost:8080` loads
7. Push tag `v0.0.1-test` → `release.yml` creates draft GitHub Release
