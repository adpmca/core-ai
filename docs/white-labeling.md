# White-Labeling Guide

Diva AI supports two white-labeling paths depending on how thoroughly you need to rebrand.

| Need | Path |
|------|------|
| Change product name in UI | Configuration-driven (no fork) |
| Change JWT issuer / audience | Configuration-driven (no fork) |
| Change localStorage key prefix | Configuration-driven (no fork) |
| Change Docker service names | Edit `docker-compose.yml` manually |
| Publish NuGet packages under own namespace | Full namespace rebrand script |
| Internal policy — no "Diva" anywhere in C# code | Full namespace rebrand script |
| Pull security patches from upstream | Configuration-driven (stays on upstream) |

---

## Configuration-Driven Branding (Recommended)

No fork required. Set environment variables and redeploy.

### Backend (.NET API)

| Variable | Default | Effect |
|----------|---------|--------|
| `AppBranding__ProductName` | `Diva AI` | Display name (not yet used server-side — informational) |
| `AppBranding__Slug` | `diva` | Platform API key prefix (`{slug}_…`). **Changing after go-live invalidates all existing API keys.** |
| `AppBranding__ApiAudience` | `diva-api` | JWT `aud` claim for locally-issued tokens. **Changing invalidates all active JWTs — users must re-login.** |
| `AppBranding__LocalIssuer` | `diva-local` | JWT `iss` claim for locally-issued tokens. **Changing invalidates all active JWTs.** |

Example `docker-compose.yml` override for AcmeCorp:

```yaml
services:
  api:
    environment:
      - AppBranding__ProductName=AcmeCorp AI
      - AppBranding__Slug=acme
      - AppBranding__ApiAudience=acme-api
      - AppBranding__LocalIssuer=acme-local
```

### Admin Portal (React)

| Variable | Default | Effect |
|----------|---------|--------|
| `VITE_APP_NAME` | `Diva AI` | Sidebar title, login page heading, browser tab title |
| `VITE_APP_SLUG` | `diva` | localStorage key prefix (`{slug}_token`, etc.). **Changing after go-live clears all browser sessions.** |

Example `admin-portal/.env`:

```
VITE_APP_NAME=AcmeCorp AI
VITE_APP_SLUG=acme
VITE_API_URL=https://api.acmecorp.example.com
VITE_AUTH_ENABLED=true
```

### Worked Example — "AcmeCorp AI"

1. Copy `.env.example` to `.env`
2. Set:
   ```
   AppBranding__ProductName=AcmeCorp AI
   AppBranding__Slug=acme
   AppBranding__ApiAudience=acme-api
   AppBranding__LocalIssuer=acme-local
   ```
3. In `admin-portal/.env`:
   ```
   VITE_APP_NAME=AcmeCorp AI
   VITE_APP_SLUG=acme
   ```
4. Rebuild frontend: `cd admin-portal && npm run build`
5. Redeploy — login page and sidebar now show "AcmeCorp AI"

---

## Full Namespace Rebrand

Use this path when you need C# namespaces to read `AcmeCorp.*` (e.g. for internal NuGet publishing) or when your organisation has a strict policy against any "Diva" branding in source code.

### PowerShell (Windows / cross-platform PS7)

```powershell
./tools/rebrand.ps1 -NewName AcmeCorp -NewSlug acme
# optional: -NewApiAudience acme-api
```

### Bash (Linux / macOS / CI)

```bash
./tools/rebrand.sh AcmeCorp acme acme-api
```

Both scripts:
- Validate inputs (no spaces in slug, clean git working tree)
- Create a `rebrand/acme` branch
- Replace all namespace prefixes, JWT strings, display strings, localStorage prefixes
- Rename `src/Diva.*` directories and `Diva.slnx` solution file
- Commit the result

### Build verification after rebrand

```bash
dotnet build AcmeCorp.slnx
dotnet test AcmeCorp.slnx
cd admin-portal && npm run build
```

---

## Staying Current with Upstream

### Configuration-driven deployments

You are on the upstream repo — pull and redeploy normally. No merge conflicts.

### Full rebrand fork

The rebrand branch diverges from `main`. To pull in upstream patches:

```bash
git fetch upstream
git checkout rebrand/acme
git merge upstream/main
# Resolve any conflicts (typically just new "Diva.*" namespaces to rename)
# Re-run the rebrand script on changed files if needed, then build/test
```

Recommended sync cadence:

| Release type | Action |
|---|---|
| Patch (`1.0.x`) | Merge within 24 h — may contain security fixes |
| Minor (`1.x.0`) | Test rebrand compatibility, merge within 1 week |
| Major (`x.0.0`) | Audit for API changes before merging |

---

## Caveats

- **`AppBranding__Slug` after go-live** — changing the slug changes the localStorage key prefix (`diva_token` → `acme_token`). Existing browser sessions stop working; users must re-login.
- **`AppBranding__LocalIssuer` / `ApiAudience` after go-live** — changing either invalidates all locally-issued JWTs. Users of local auth (username/password and SSO-bridged tokens) must re-login.
- **Platform API key prefix** — existing `diva_…` keys are stored in the database. After changing the slug, those keys will fail validation because the prefix no longer matches. Re-issue new keys with the updated prefix.
- **`Credentials__MasterKey` rotation is separate** — AES-256-GCM key for MCP credential encryption. See `SECURITY.md` for rotation procedure.
