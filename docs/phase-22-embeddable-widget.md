# Phase 22: Embeddable Chat Widget

> **Status:** `[x]` Complete — 2026-04-20
> **Depends on:** [phase-03-oauth-tenant.md](phase-03-oauth-tenant.md), [phase-04-database.md](phase-04-database.md), [phase-06-tenant-admin.md](phase-06-tenant-admin.md), [phase-10-api-host.md](phase-10-api-host.md)
> **Blocks:** Nothing (additive)
> **Projects:** `Diva.Core`, `Diva.Infrastructure`, `Diva.TenantAdmin`, `Diva.Host`, `admin-portal`
> **Tests:** `tests/Diva.TenantAdmin.Tests` — 10 new tests

---

## Goal

Enable any third-party website to embed a Diva AI chat widget with a single `<script>` tag (Intercom/Drift-style). Each widget instance is bound to a specific tenant agent. When the host website is an SSO provider the widget auto-logs the user in transparently via a `postMessage` token exchange. If no SSO is available, the widget falls back to an anonymous session scoped to that agent only.

---

## Architecture

```
Host website                           Diva API (e.g. api.diva.com)
──────────────────                     ────────────────────────────────────────
<script src="/widget.js"               GET /widget-ui?id={widgetId}
  data-widget-id="abc123">              → serves wwwroot/widget/index.html
                                              ↓ SPA loads (11.5 kB bundle)
  ↓ creates launcher button             GET /api/widget/{id}/init   → WidgetInitResponse
  ↓ creates hidden <iframe>             POST /api/widget/{id}/auth  → Diva JWT (SSO path)
  ↑ postMessage SSO token              POST /api/widget/{id}/session → Diva JWT (anon path)
                                        POST /api/agents/{id}/invoke/stream  (same-origin, no CORS)
```

**Key insight:** the widget SPA is served from the API origin via `GET /widget-ui`, so the `<iframe>` is **same-origin** with the API. All `/api/agents/{id}/invoke/stream` calls from inside the iframe have no CORS requirements. Only the public `/api/widget/*` endpoints need CORS, which is handled by the `Widget` policy (`SetIsOriginAllowed(_ => true)`). The controller validates the `Origin` header against the per-widget `AllowedOriginsJson` DB column.

---

## Implemented Components

### Phase 22a — DB Entity + Migration

#### `src/Diva.Infrastructure/Data/Entities/WidgetConfigEntity.cs` (new)

```csharp
public class WidgetConfigEntity : ITenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int TenantId { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? AllowedOriginsJson { get; set; }    // JSON string[] — exact origins only, port-sensitive
    public int? SsoConfigId { get; set; }              // FK to TenantSsoConfigs (optional)
    public bool AllowAnonymous { get; set; } = true;
    public string? WelcomeMessage { get; set; }
    public string? PlaceholderText { get; set; }
    public string? ThemeJson { get; set; }             // JSON WidgetTheme; null → Light preset at read time
    public bool RespectSystemTheme { get; set; } = true;
    public bool ShowBranding { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
}
```

#### `src/Diva.Infrastructure/Data/DivaDbContext.cs` (modified)

- Added `DbSet<WidgetConfigEntity> WidgetConfigs => Set<WidgetConfigEntity>();`
- In `OnModelCreating`: `HasKey(e => e.Id)`, `HasQueryFilter(e => _currentTenantId == 0 || e.TenantId == _currentTenantId)`, `HasIndex(e => new { e.TenantId, e.IsActive })`

#### `src/Diva.Infrastructure/Data/Migrations/20260418000000_AddWidgetConfigs.cs` (new)

Creates `WidgetConfigs` table with all 15 columns plus index `IX_WidgetConfigs_TenantId_IsActive`.

#### `src/Diva.Infrastructure/Data/Migrations/20260418000000_AddWidgetConfigs.Designer.cs` (new)

Required for EF migration discovery — contains `[DbContext(typeof(DivaDbContext))]` + `[Migration("20260418000000_AddWidgetConfigs")]` attributes and full `BuildTargetModel` snapshot.

#### `src/Diva.Infrastructure/Data/Migrations/DivaDbContextModelSnapshot.cs` (modified)

Added `WidgetConfigEntity` table block before the relationships section.

---

### Phase 22b — Core Models + Theme System

#### `src/Diva.Core/Models/Widgets/WidgetTheme.cs` (new)

```csharp
public record WidgetTheme
{
    public string Background { get; init; } = "#ffffff";
    public string Surface { get; init; } = "#f9fafb";
    public string Border { get; init; } = "#e5e7eb";
    public string Primary { get; init; } = "#6366f1";
    public string PrimaryText { get; init; } = "#ffffff";
    public string Text { get; init; } = "#111827";
    public string TextMuted { get; init; } = "#6b7280";
    public string FontFamily { get; init; } = "system-ui, sans-serif";
    public string FontSize { get; init; } = "14px";
    public string AgentBubbleBg { get; init; } = "#f3f4f6";
    public string AgentBubbleText { get; init; } = "#111827";
    public string HeaderBg { get; init; } = "#6366f1";
    public string HeaderText { get; init; } = "#ffffff";
    public string InputBg { get; init; } = "#ffffff";
    public string InputBorder { get; init; } = "#d1d5db";
    public string InputText { get; init; } = "#111827";
    public int LauncherSize { get; init; } = 56;
    public string? Preset { get; init; }               // "light" | "dark" | "custom" — informational only

    public static WidgetTheme Light => new() { Preset = "light" };
    public static WidgetTheme Dark => new() { /* dark palette */ Preset = "dark" };
}
```

Theme is always stored as full JSON so per-tenant color customisation from a preset baseline is supported without loss. `Preset` is informational — the server never re-applies a preset from this field.

**System dark mode:** `RespectSystemTheme=true` tells the widget SPA to swap to the `DARK_PRESET` CSS variables when `window.matchMedia('(prefers-color-scheme: dark)').matches`. A `change` event listener re-applies dynamically.

#### `src/Diva.Core/Models/Widgets/WidgetDtos.cs` (new)

| Type | Purpose |
|------|---------|
| `WidgetConfigDto` | Admin list/edit response — full config including theme |
| `CreateWidgetRequest` | Admin create/update request body |
| `WidgetInitResponse` | Public `GET /init` — no secrets; includes `AgentName` looked up from `AgentDefinitions` |
| `WidgetAuthRequest` | SSO token exchange body |
| `WidgetAuthResponse` | Diva JWT + userId + expiry |
| `WidgetSessionResponse` | Anonymous session JWT + sessionId + expiry |

---

### Phase 22c — Service Layer

#### `src/Diva.TenantAdmin/Services/IWidgetConfigService.cs` (new)

```csharp
public interface IWidgetConfigService
{
    Task<List<WidgetConfigDto>> GetForTenantAsync(int tenantId, CancellationToken ct = default);
    Task<WidgetConfigEntity?> GetByIdAsync(string widgetId, CancellationToken ct = default);
    Task<WidgetConfigDto> CreateAsync(int tenantId, CreateWidgetRequest request, CancellationToken ct = default);
    Task<WidgetConfigDto> UpdateAsync(int tenantId, string id, CreateWidgetRequest request, CancellationToken ct = default);
    Task DeleteAsync(int tenantId, string id, CancellationToken ct = default);
}
```

`GetByIdAsync` is annotated as bypassing the tenant query filter — it uses `_db.CreateDbContext()` (null → `tenantId=0`) so public widget endpoints can look up any widget regardless of tenant.

#### `src/Diva.TenantAdmin/Services/WidgetConfigService.cs` (new)

- `ThemeJson=null` → `WidgetTheme.Light` returned (never persists null to the DTO)
- `AllowedOrigins` serialised as `string[]` JSON; empty array stored as `null`
- `DeleteAsync` is idempotent — silently returns if widget not found

---

### Phase 22d — Auth Extension

#### `src/Diva.Infrastructure/Auth/LocalAuthService.cs` (modified)

Added `IssueWidgetAnonymousJwt(int tenantId, string userId, string agentId, TimeSpan ttl)`:

- Issues a JWT with `agent_access` claim = `agentId`
- Role = `"user"`
- No email claim
- TTL configurable (1 h for anonymous widget sessions)

The `agent_access` claim enables future throttling of anonymous widget calls at the invoke endpoint without changing the existing auth pipeline.

---

### Phase 22e — Backend API

#### `src/Diva.Host/Controllers/WidgetController.cs` (new)

All endpoints are `[AllowAnonymous]` + `[EnableCors("Widget")]`.

| Endpoint | Implementation |
|----------|---------------|
| `GET /widget-ui` | `PhysicalFile(wwwroot/widget/index.html, "text/html")` — serves the SPA shell |
| `GET /api/widget/{widgetId}/init` | Loads widget config; joins `AgentDefinitions` for `DisplayName`; returns `WidgetInitResponse` (no secrets) |
| `POST /api/widget/{widgetId}/auth` | Validates `Origin` → loads `TenantSsoConfigEntity` → `ISsoTokenValidator.ValidateAsync` → `ITenantClaimsExtractor.Extract` → `ILocalAuthService.IssueSsoJwt` → `WidgetAuthResponse` |
| `POST /api/widget/{widgetId}/session` | Validates `Origin` + `AllowAnonymous` → `ILocalAuthService.IssueWidgetAnonymousJwt` → `WidgetSessionResponse` |

**Origin validation:** `IsOriginAllowed()` helper checks `Request.Headers.Origin` against `AllowedOriginsJson`. Requests with no `Origin` header (server-to-server) are allowed through.

**Expiry check:** `IsExpired(widget.ExpiresAt)` returns 404 for expired widgets.

#### `src/Diva.Host/Controllers/AdminController.cs` (modified)

Added widget admin region with `EffectiveTenantId` pattern:

| Endpoint | Returns |
|----------|---------|
| `GET /api/admin/widgets?tenantId=` | `List<WidgetConfigDto>` |
| `POST /api/admin/widgets?tenantId=` | `WidgetConfigDto` (201) |
| `PUT /api/admin/widgets/{id}?tenantId=` | `WidgetConfigDto` |
| `DELETE /api/admin/widgets/{id}?tenantId=` | 204 |

#### `src/Diva.Host/Program.cs` (modified)

```csharp
// Widget CORS policy — origin validated inside controller against AllowedOriginsJson
options.AddPolicy("Widget", policy =>
    policy.SetIsOriginAllowed(_ => true)
          .AllowAnyHeader()
          .AllowAnyMethod()
          .AllowCredentials());

// DI
builder.Services.AddScoped<IWidgetConfigService, WidgetConfigService>();
```

---

### Phase 22f — Embed Script

#### `src/Diva.Host/wwwroot/widget.js` (new)

Vanilla JS (no framework), IIFE, ~120 lines. Runs on the **host** website:

1. Reads `data-widget-id` and `data-position` attributes from `<script>` tag
2. Injects a circular launcher button (fixed, bottom-right/left) with hover scale + shadow transitions
3. Injects a hidden `<iframe>` pointing at `{API}/widget-ui?id={widgetId}`; display toggled with CSS opacity + translateY transition
4. Toggles open/close on launcher click; ARIA label updated accordingly
5. Listens for `DIVA_SSO_REQUEST` from iframe → calls `window.__divaSsoProvider()` if registered → posts `DIVA_SSO_TOKEN` back (handles async providers + errors)
6. Listens for `DIVA_CLOSE` from iframe → hides iframe
7. Listens for `window.resize` → adjusts iframe width for narrow viewports (`< 480px`)

**Host-side SSO registration:**
```html
<script>
  window.__divaSsoProvider = async () => await myAuth.getAccessToken();
</script>
<script src="https://api.diva.example.com/widget.js" data-widget-id="abc123"></script>
```

---

### Phase 22g — Widget SPA

Multi-entry Vite build: second entry alongside the admin portal. Bundle size: **11.5 kB** / gzipped **4.0 kB**.

#### `admin-portal/vite.config.ts` (modified)

```ts
build: {
  rollupOptions: {
    input: {
      main: resolve(__dirname, 'index.html'),
      widget: resolve(__dirname, 'widget.html'),
    },
    output: {
      entryFileNames: '[name]/[name]-[hash].js',
      chunkFileNames: 'chunks/chunk-[hash].js',
      assetFileNames: 'assets/[name]-[hash][extname]',
    },
  },
},
```

#### `admin-portal/widget.html` (new)

Minimal HTML entry point. Inline reset CSS scoped to `html, body, #widget-root`. No Tailwind (too large for widget bundle) — all widget styling is inline React style objects using `var(--diva-*)` CSS custom properties.

#### `admin-portal/src/widget/types.ts` (new)

| Export | Description |
|--------|-------------|
| `WidgetTheme` | TypeScript mirror of C# `WidgetTheme` record |
| `WidgetInitResponse` | TypeScript mirror of C# DTO |
| `AgentStreamChunk` | Mirrors `AgentStreamChunk` from `Diva.Core` — `type`, `content`, `delta`, `sessionId` |
| `ChatMessage` | `{ id, role: 'user' \| 'agent', content, streaming? }` |
| `LIGHT_PRESET` / `DARK_PRESET` | Static theme constants matching C# `WidgetTheme.Light` / `WidgetTheme.Dark` |

#### `admin-portal/src/widget/main.tsx` (new)

Reads `?id=` from `window.location.search`, mounts `<WidgetApp widgetId={id} />` on `#widget-root`. No `StrictMode` (avoids double-mount SSO request during dev).

#### `admin-portal/src/widget/WidgetApp.tsx` (new)

State machine: `loading` → `authing` → `ready` | `denied`

1. **Load config:** `GET /api/widget/{id}/init` → `WidgetInitResponse`
2. **Apply theme immediately** (before auth — no FOUC): sets all `--diva-*` CSS custom properties on `:root`
3. **Watch dark mode:** `window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', ...)` re-applies theme
4. **Stored session check:** reads `sessionStorage['diva_widget_{id}']`; skips auth if token is > 5 min from expiry
5. **SSO flow:** posts `{type: 'DIVA_SSO_REQUEST'}` to `window.parent`; waits 3 s for `DIVA_SSO_TOKEN`; on success: `POST /api/widget/{id}/auth`; on null/timeout: falls through to anonymous
6. **Anonymous flow:** `POST /api/widget/{id}/session` if `allowAnonymous`; else state → `denied`
7. **JWT persistence:** stored in `sessionStorage` with `expiresAt`; within-5-min expiry triggers silent re-auth

#### `admin-portal/src/widget/WidgetChat.tsx` (new)

Full streaming chat UI. All CSS via inline style objects using `var(--diva-*)` properties.

**Layout:**
- Header bar: agent name + green presence dot + close button
- Scrollable message list (auto-scroll to bottom on new messages/typing)
- Input bar: `<textarea>` (Enter to send, Shift+Enter for newline) + circular send button

**Streaming:**
- `fetch('/api/agents/{agentId}/invoke/stream', { Authorization: 'Bearer {jwt}' })`
- `ReadableStream` reader with `TextDecoder`; line-by-line SSE parsing
- `text_delta` → appends delta to in-progress agent bubble with streaming cursor `▍`
- `final_response` → commits full content, removes cursor
- `iteration_start` → shows typing indicator (3-dot bounce animation)
- `error` → inline error bubble
- `done` → clears streaming flag

**Session persistence:**
- `sessionId` stored in `sessionStorage['diva_session_{widgetId}']`
- Sent as `sessionId` in each invoke request body for multi-turn continuity

**Close button:** posts `{type: 'DIVA_CLOSE'}` to `window.parent`; `widget.js` hides iframe.

---

### Phase 22h — Admin UI

#### `admin-portal/src/api.ts` (modified)

Added types `WidgetThemeDto`, `WidgetConfigDto`, `CreateWidgetRequest` and API functions `listWidgets`, `createWidget`, `updateWidget`, `deleteWidget`.

#### `admin-portal/src/components/WidgetManager.tsx` (new)

Route: `/settings/widgets`

- Lists all widgets via `GET /api/admin/widgets?tenantId={id}`
- Card per widget: name badge (Active/Inactive), SSO badge, Anon badge, agent ID, origins, expiry
- Actions: copy embed code (clipboard), edit (opens `WidgetEditor`), delete (confirm)
- Embed code snippet: basic `<script>` tag; if `ssoConfigId` set, includes `__divaSsoProvider` stub

#### `admin-portal/src/components/WidgetEditor.tsx` (new)

Modal dialog — create or edit mode.

**Fields:**
- Name, Agent (dropdown from `/api/agents`), Allowed Origins (textarea, one per line)
- SSO Config (dropdown from `/api/admin/sso-configs`, optional)
- Expires At (date picker, optional)
- Welcome Message, Placeholder Text
- Allow Anonymous, Respect System Dark Mode, Show Branding (checkboxes)

**Theme section:**
- Preset toggle: `Light` | `Dark` | `Custom`
  - Selecting Light/Dark populates all 12 color fields with preset defaults; preset field set to `"custom"` on any manual color change
- 12 color pickers (native `<input type="color">` + hex text input)
- Font Family (text), Font Size (select: 12px / 14px / 16px)
- **Live preview panel** — `WidgetPreview` sub-component renders a 260×340 mock widget window using the current theme colors as inline styles

#### `admin-portal/src/App.tsx` (modified)

Added `<Route path="settings/widgets" element={<WidgetManager />} />`

#### `admin-portal/src/components/layout/app-sidebar.tsx` (modified)

Added "Chat Widgets" nav item (`Code2` icon) under Settings, after "A2A Protocol".

---

### Phase 22i — Tests

#### `tests/Diva.TenantAdmin.Tests/WidgetConfigServiceTests.cs` (new)

10 tests. Real SQLite in-memory (`DataSource=:memory:`), no mocked DbContext.

| Test | Covers |
|------|--------|
| `CreateAsync_StoresWidget_ReturnsDto` | Full field mapping, IsActive=true, null theme → Light defaults |
| `CreateAsync_WithCustomTheme_RoundTripsColors` | ThemeJson serialisation round-trip (custom colors + preset field) |
| `GetForTenantAsync_ReturnsTenantScopedWidgets_CrossTenantNotVisible` | EF query filter — tenant 1 can't see tenant 2's widgets |
| `GetByIdAsync_BypassesTenantFilter_ReturnsAnyTenantWidget` | `tenantId=0` context bypasses filter for public endpoint use |
| `GetByIdAsync_ReturnsNull_WhenNotFound` | Null return on unknown ID |
| `UpdateAsync_ChangesFields_ReturnsUpdatedDto` | All updatable fields; AllowedOrigins array changes |
| `UpdateAsync_ThrowsKeyNotFoundException_WhenNotFound` | Update against bad ID throws |
| `DeleteAsync_RemovesWidget` | Widget absent from list after delete |
| `DeleteAsync_IsIdempotent_WhenNotFound` | No exception on delete of non-existent widget |
| `CreateAsync_NullTheme_DefaultsToLightPreset` | ThemeJson=null → WidgetTheme.Light colors returned |

---

## Security Notes

| Concern | Mitigation |
|---------|-----------|
| postMessage origin spoofing | `widget.js` listener checks `e.source === iframe.contentWindow` before trusting any message |
| Arbitrary origin CORS | `Widget` CORS policy allows all origins at ASP.NET Core level; controller enforces DB `AllowedOriginsJson` (exact, case-insensitive, port-sensitive) |
| SSO token theft | SSO JWT is exchanged server-side; the resulting Diva JWT is short-lived (8 h) and scoped to the widget's tenant |
| Anonymous session abuse | Anonymous JWT carries `agent_access` claim = single `agentId`; cannot invoke other agents; future rate limiting can key on `widgetId` claim |
| Widget expiry | `ExpiresAt` enforced on every request to init/auth/session endpoints — 404 returned after expiry |
| Inbound API key not forwarded | Consistent with the 2026-04-16 MCP credential fix: `X-API-Key` is never forwarded to external services |

---

## Verification Checklist

- [ ] Migration applied: `WidgetConfigs` table created with all 15 columns and index
- [ ] `GET /api/widget/{id}/init` returns JSON with full theme object and `agentName`
- [ ] `POST /api/widget/{id}/session` with valid origin → JWT with `agent_access` claim; userId starts with `anon:`
- [ ] Same call with unlisted `Origin` header → 403
- [ ] `GET /widget-ui?id={id}` serves `wwwroot/widget/index.html` (or 404 if not built)
- [ ] Admin portal: Settings → Chat Widgets → Create → select Dark preset → verify colors populate; change Primary → preset switches to Custom
- [ ] Copy embed code → paste into a local HTML page served on an allowed origin → launcher appears, anonymous JWT issued, agent responds with streaming text
- [ ] Close button in widget header → iframe disappears in host page
- [ ] Resize browser to < 480 px → iframe width adjusts to viewport
- [ ] OS dark mode toggle → widget re-themes automatically when `RespectSystemTheme=true`
- [ ] `dotnet test tests/Diva.TenantAdmin.Tests --filter WidgetConfig` → 10 passed

---

## Build Pipeline Note

After `npm run build` in `admin-portal/`, the widget SPA lands in `admin-portal/dist/widget/`. This directory must be copied to `src/Diva.Host/wwwroot/widget/` for the `GET /widget-ui` endpoint to serve it. Add to your publish pipeline or document as a manual step:

```bash
# From solution root
cd admin-portal && npm run build
cp -r dist/widget/ ../src/Diva.Host/wwwroot/widget/
```

A future `Diva.Host.csproj` `<Target Name="CopyWidgetDist" AfterTargets="Build">` can automate this.

---

## File Index

| File | Status | Description |
|------|--------|-------------|
| `src/Diva.Infrastructure/Data/Entities/WidgetConfigEntity.cs` | New | DB entity, implements `ITenantEntity` |
| `src/Diva.Infrastructure/Data/DivaDbContext.cs` | Modified | DbSet + query filter + index |
| `src/Diva.Infrastructure/Data/Migrations/20260418000000_AddWidgetConfigs.cs` | New | EF migration |
| `src/Diva.Infrastructure/Data/Migrations/20260418000000_AddWidgetConfigs.Designer.cs` | New | EF migration designer |
| `src/Diva.Infrastructure/Data/Migrations/DivaDbContextModelSnapshot.cs` | Modified | Snapshot updated |
| `src/Diva.Infrastructure/Auth/LocalAuthService.cs` | Modified | `IssueWidgetAnonymousJwt` added |
| `src/Diva.Core/Models/Widgets/WidgetTheme.cs` | New | Theme record + Light/Dark presets |
| `src/Diva.Core/Models/Widgets/WidgetDtos.cs` | New | All widget DTOs and request models |
| `src/Diva.TenantAdmin/Services/IWidgetConfigService.cs` | New | Service interface |
| `src/Diva.TenantAdmin/Services/WidgetConfigService.cs` | New | Service implementation |
| `src/Diva.Host/Controllers/WidgetController.cs` | New | Public widget endpoints |
| `src/Diva.Host/Controllers/AdminController.cs` | Modified | Widget admin CRUD region |
| `src/Diva.Host/Program.cs` | Modified | Widget CORS policy + DI registration |
| `src/Diva.Host/wwwroot/widget.js` | New | Vanilla JS embed script |
| `src/Diva.Host/wwwroot/widget/` | Runtime | Widget SPA build output (git-ignored) |
| `admin-portal/vite.config.ts` | Modified | Multi-entry build config |
| `admin-portal/widget.html` | New | Widget SPA HTML entry point |
| `admin-portal/src/widget/main.tsx` | New | Widget SPA mount |
| `admin-portal/src/widget/types.ts` | New | TypeScript types + preset constants |
| `admin-portal/src/widget/WidgetApp.tsx` | New | Auth flow + theme application |
| `admin-portal/src/widget/WidgetChat.tsx` | New | SSE streaming chat UI |
| `admin-portal/src/api.ts` | Modified | Widget types + API functions |
| `admin-portal/src/components/WidgetManager.tsx` | New | Admin list page |
| `admin-portal/src/components/WidgetEditor.tsx` | New | Admin create/edit modal |
| `admin-portal/src/App.tsx` | Modified | `/settings/widgets` route |
| `admin-portal/src/components/layout/app-sidebar.tsx` | Modified | "Chat Widgets" nav item |
| `tests/Diva.TenantAdmin.Tests/WidgetConfigServiceTests.cs` | New | 10 integration tests |
