# Phase 12: Admin Portal UI

> **Status:** `[x]` Done — all major features complete (Docker Gateway panel, streaming, rule review, business rules, dashboard)
> **Depends on:** [phase-10-api-host.md](phase-10-api-host.md) (API must be running)
> **Project:** `admin-portal/` (React + Vite + TypeScript)

---

## Goal

Build the React admin portal that lets tenant admins configure agents, business rules, prompts, and review learned rules — without touching code or restarting services.

---

## Tech Stack

| Tool | Version | Purpose |
|------|---------|---------|
| React | 18+ | UI framework |
| Vite | 5+ | Build tool |
| TypeScript | 5+ | Type safety |
| TanStack Query | 5+ | Data fetching + caching |
| TanStack Router | 1+ | Type-safe routing |
| shadcn/ui | latest | Component library |
| Tailwind CSS | 3+ | Styling |
| React Hook Form | 7+ | Forms |
| Zod | 3+ | Schema validation |

---

## Bootstrap Commands

```bash
npm create vite@latest admin-portal -- --template react-ts
cd admin-portal
npm install @tanstack/react-query @tanstack/react-router
npm install shadcn-ui tailwindcss
npx shadcn@latest init
npm install react-hook-form zod @hookform/resolvers
npm install @microsoft/signalr  # for real-time streaming
```

---

## File Structure

```
admin-portal/src/
├── main.tsx
├── App.tsx
├── api/
│   └── adminApi.ts              ← all API calls
├── auth/
│   └── useAuth.ts               ← tenant/user context from JWT
├── pages/
│   ├── Dashboard.tsx            ← usage, cost, token metrics
│   ├── BusinessRules.tsx        ← view/create/edit tenant rules
│   ├── PromptEditor.tsx         ← edit system prompt overrides
│   ├── AgentConfig.tsx          ← enable/disable agents per tenant
│   ├── AgentBuilder.tsx         ← create/edit/publish dynamic agents
│   └── PendingRules.tsx         ← approve/reject learned rules
├── components/
│   ├── RuleEditor.tsx
│   ├── PromptPreview.tsx
│   ├── AgentToggle.tsx
│   ├── AgentTestPanel.tsx       ← test agent before publishing
│   └── ConfidenceBadge.tsx
└── lib/
    └── utils.ts
```

---

## adminApi.ts

```typescript
const BASE = import.meta.env.VITE_API_URL ?? 'http://localhost:8080';

async function apiCall<T>(path: string, options?: RequestInit): Promise<T> {
  const token = localStorage.getItem('access_token');
  const res = await fetch(`${BASE}${path}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${token}`,
      ...options?.headers,
    },
  });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

export const api = {
  // Business Rules
  getRules:    (tenantId: number) =>
    apiCall<BusinessRule[]>(`/api/admin/business-rules/${tenantId}`),
  createRule:  (tenantId: number, rule: CreateRuleDto) =>
    apiCall<BusinessRule>(`/api/admin/business-rules/${tenantId}`, { method: 'POST', body: JSON.stringify(rule) }),
  updateRule:  (tenantId: number, ruleId: number, rule: UpdateRuleDto) =>
    apiCall<void>(`/api/admin/business-rules/${tenantId}/${ruleId}`, { method: 'PUT', body: JSON.stringify(rule) }),
  deleteRule:  (tenantId: number, ruleId: number) =>
    apiCall<void>(`/api/admin/business-rules/${tenantId}/${ruleId}`, { method: 'DELETE' }),

  // Prompt Overrides
  getPrompts:  (tenantId: number) =>
    apiCall<PromptOverride[]>(`/api/admin/prompts/${tenantId}`),
  savePrompt:  (tenantId: number, override: PromptOverrideDto) =>
    apiCall<PromptOverride>(`/api/admin/prompts/${tenantId}`, { method: 'POST', body: JSON.stringify(override) }),

  // Agents
  getAgents:   (tenantId: number) =>
    apiCall<AgentDefinition[]>(`/api/admin/agents/${tenantId}`),
  createAgent: (tenantId: number, def: AgentDefinitionDto) =>
    apiCall<AgentDefinition>(`/api/admin/agents/${tenantId}`, { method: 'POST', body: JSON.stringify(def) }),
  updateAgent: (tenantId: number, agentId: string, def: AgentDefinitionDto) =>
    apiCall<AgentDefinition>(`/api/admin/agents/${tenantId}/${agentId}`, { method: 'PUT', body: JSON.stringify(def) }),
  publishAgent:(tenantId: number, agentId: string) =>
    apiCall<void>(`/api/admin/agents/${tenantId}/${agentId}/publish`, { method: 'POST' }),

  // Learned Rules
  getPendingRules: (tenantId: number) =>
    apiCall<LearnedRule[]>(`/api/admin/learned-rules/${tenantId}`),
  approveRule: (tenantId: number, ruleId: number) =>
    apiCall<void>(`/api/admin/learned-rules/${tenantId}/${ruleId}/approve`, { method: 'POST' }),
  rejectRule:  (tenantId: number, ruleId: number) =>
    apiCall<void>(`/api/admin/learned-rules/${tenantId}/${ruleId}/reject`, { method: 'POST' }),
};
```

---

## BusinessRules.tsx

```tsx
export function BusinessRulesPage() {
  const { tenantId } = useAuth();
  const { data: rules, refetch } = useQuery({
    queryKey: ['rules', tenantId],
    queryFn:  () => api.getRules(tenantId),
  });

  const createMutation = useMutation({
    mutationFn: (rule: CreateRuleDto) => api.createRule(tenantId, rule),
    onSuccess:  () => refetch(),
  });

  return (
    <div className="p-6 space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Business Rules</h1>
        <Button onClick={() => setShowCreate(true)}>Add Rule</Button>
      </div>

      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Category</TableHead>
            <TableHead>Key</TableHead>
            <TableHead>Agent Type</TableHead>
            <TableHead>Prompt Injection</TableHead>
            <TableHead>Active</TableHead>
            <TableHead>Actions</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rules?.map(rule => (
            <TableRow key={rule.id}>
              <TableCell><Badge>{rule.ruleCategory}</Badge></TableCell>
              <TableCell className="font-mono text-sm">{rule.ruleKey}</TableCell>
              <TableCell>{rule.agentType}</TableCell>
              <TableCell className="max-w-md truncate">{rule.promptInjection}</TableCell>
              <TableCell><Switch checked={rule.isActive} /></TableCell>
              <TableCell>
                <Button size="sm" onClick={() => editRule(rule)}>Edit</Button>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
```

---

## AgentBuilder.tsx (Dynamic Agent Creation)

Key features:
- System prompt editor with `{{variable}}` syntax highlighting
- Capability tags (multi-input)
- Tool binding selection (which MCP tools are available)
- Temperature/MaxIterations sliders
- Advanced config defaults loaded from `GET /api/config/agent-defaults`
- Per-agent advanced settings: verification mode, max continuations, context window override, tool filtering, pipeline stages, stage instructions, custom variables
- Draft/Publish workflow
- **Test panel**: send a sample query to the agent before publishing

```tsx
export function AgentBuilderPage() {
  const form = useForm<AgentDefinitionDto>({
    resolver: zodResolver(agentDefinitionSchema),
    defaultValues: { temperature: 0.7, maxIterations: 10, status: 'Draft' }
  });

  return (
    <div className="grid grid-cols-2 gap-6">
      {/* Left: Form */}
      <Form {...form}>
        <FormField name="name" render={/* ... */} />
        <FormField name="agentType" render={/* ... */} />
        <FormField name="systemPrompt" render={
          ({ field }) => <Textarea {...field} className="font-mono h-64" />
        } />
        <FormField name="capabilities" render={/* tag input */} />
        <FormField name="temperature" render={/* slider */} />
        <div className="flex gap-2">
          <Button type="submit" variant="outline">Save Draft</Button>
          <Button type="button" onClick={publish}>Publish</Button>
        </div>
      </Form>

      {/* Right: Test Panel */}
      <AgentTestPanel agentId={savedAgentId} tenantId={tenantId} />
    </div>
  );
}
```

### Current implementation notes

- The current portal uses a lightweight custom API client in `src/api.ts` rather than the older `adminApi.ts` sketch shown above.
- `AgentBuilder.tsx` loads global defaults from the backend so placeholders reflect the real `appsettings` values instead of hardcoded fallback text.
- The test chat is implemented in `components/AgentChat.tsx` and is used as the live agent test surface.

## AgentChat.tsx (Agent Test Window)

Current test-window capabilities:
- Streams all SSE events from `api.streamAgent()` and keeps live per-iteration state
- Supports a `Detailed` mode that shows:
  - live event timeline with timestamps and SSE event summaries
  - full thinking text without truncation
  - full tool input/output without height clipping
  - auto-expanded completed iteration traces
- Renders agent responses as sanitized HTML using `DOMPurify`
- Keeps user messages and error messages as plain text
- Supports per-session model override from the configured available model list

Important implementation detail:
- Any new SSE event added on the backend must be mirrored in both `src/api.ts` and the `handleChunk` switch in `AgentChat.tsx`.

---

## PromptEditor.tsx

```tsx
export function PromptEditorPage() {
  const [preview, setPreview] = useState('');
  const [agentType, setAgentType] = useState('Analytics');
  const [section, setSection] = useState('react-agent');
  const [customText, setCustomText] = useState('');
  const [mergeMode, setMergeMode] = useState<'Append' | 'Prepend' | 'Replace'>('Append');

  // Preview: call API to render final merged prompt
  const previewMutation = useMutation({
    mutationFn: () => api.previewPrompt(tenantId, { agentType, section, customText, mergeMode }),
    onSuccess: (data) => setPreview(data.renderedPrompt),
  });

  return (
    <div className="grid grid-cols-2 gap-6 h-screen p-6">
      <div className="space-y-4">
        <Select value={agentType} onValueChange={setAgentType}>
          {['Analytics', 'Reservation', '*'].map(t => (
            <SelectItem key={t} value={t}>{t}</SelectItem>
          ))}
        </Select>
        <Select value={mergeMode} onValueChange={setMergeMode}>
          {['Append', 'Prepend', 'Replace'].map(m => (
            <SelectItem key={m} value={m}>{m}</SelectItem>
          ))}
        </Select>
        <Textarea value={customText} onChange={e => setCustomText(e.target.value)}
          className="font-mono h-48" placeholder="Enter custom prompt text..." />
        <div className="flex gap-2">
          <Button onClick={() => previewMutation.mutate()} variant="outline">Preview</Button>
          <Button onClick={save}>Save Override</Button>
        </div>
      </div>

      <PromptPreview content={preview} />
    </div>
  );
}
```

---

## Dashboard.tsx (Usage & Cost)

```tsx
export function DashboardPage() {
  const { tenantId } = useAuth();
  const { data: stats } = useQuery({
    queryKey: ['stats', tenantId],
    queryFn:  () => api.getStats(tenantId),
    refetchInterval: 30_000   // refresh every 30s
  });

  return (
    <div className="p-6 space-y-6">
      <div className="grid grid-cols-4 gap-4">
        <MetricCard title="Total Requests" value={stats?.totalRequests} />
        <MetricCard title="Tokens Used" value={stats?.totalTokens} />
        <MetricCard title="Est. Cost" value={`$${stats?.estimatedCost?.toFixed(2)}`} />
        <MetricCard title="Active Sessions" value={stats?.activeSessions} />
      </div>

      {/* Agent usage breakdown, cost by tenant, etc. */}
    </div>
  );
}
```

---

## TypeScript Types

```typescript
// admin-portal/src/types.ts

export interface BusinessRule {
  id: number;
  tenantId: number;
  agentType: string;
  ruleCategory: string;
  ruleKey: string;
  ruleValue?: object;
  promptInjection?: string;
  isActive: boolean;
  createdAt: string;
}

export interface AgentDefinition {
  id: string;
  tenantId: number;
  name: string;
  displayName: string;
  description: string;
  agentType: string;
  systemPrompt?: string;
  temperature: number;
  maxIterations: number;
  capabilities: string[];
  toolBindings: ToolBinding[];
  isEnabled: boolean;
  status: 'Draft' | 'Published';
  version: number;
}

export interface LearnedRule {
  id: number;
  tenantId: number;
  agentType?: string;
  ruleCategory?: string;
  promptInjection?: string;
  confidence: number;
  status: 'pending' | 'approved' | 'rejected';
  sourceSessionId?: string;
  learnedAt: string;
}
```

---

## Docker Build (admin-portal/Dockerfile)

```dockerfile
FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 3000
```

---

## Verification

- [x] `npm run dev` starts on port 5173 (Vite default, not 3000)
- [ ] Login flow: JWT from auth provider stored in localStorage
- [ ] BusinessRules page: create a rule → it appears in the table
- [ ] Edit a rule → PUT endpoint called, table updates
- [ ] PromptEditor: Preview shows merged prompt before saving
- [x] AgentBuilder: Create agent as Draft → status = Draft in DB
- [x] AgentBuilder: Publish → status = Published
- [x] AgentBuilder: MCP server config (Docker/stdio/http/sse) — full structured UI + JSON paste
- [x] AgentBuilder: `DockerGatewayPanel` — test connection, discover tools, auto-fill binding
- [x] AgentBuilder: `hasBindings` guard prevents empty/garbage bindings from being saved
- [x] PendingRules: Approve a learned rule → status changes to approved
- [x] Dashboard: Metrics update on load
- [x] Admin portal communicates with Diva API over CORS (`http://localhost:5062`)
- [x] `AgentChat`: session-aware multi-turn chat with session ID badge
- [x] `AgentChat`: iteration trace auto-expands on completion
- [x] `AgentChat`: shows `⚙️ No tools called` when iterations ran but no MCP tool was invoked
- [x] `api.ts`: `probeMcp` method + `McpToolInfo` / `McpProbeResult` types

---

## As Built — Deviations from Plan

**Tech stack actually used (simpler than plan):**

| Plan | Actual |
|---|---|
| TanStack Query + TanStack Router | Plain React state + `fetch` (no routing library) |
| shadcn/ui + Tailwind CSS | Inline CSS styles only |
| React Hook Form + Zod | Plain controlled `<input>` state |
| Port 3000 | Port **5173** (Vite default) |

**Files actually created:**
```
admin-portal/
├── src/
│   ├── api.ts                   ✓ typed API client (listAgents, getAgent, create, update, delete, invokeAgent)
│   ├── App.tsx                  ✓ single-page app with view routing via state
│   ├── main.tsx                 ✓
│   └── components/
│       ├── AgentList.tsx        ✓ lists agents with Chat/Edit/Delete actions
│       ├── AgentBuilder.tsx     ✓ create/edit agent form + MCP server config UI
│       └── AgentChat.tsx        ✓ multi-turn chat, session-aware, verification badge support
├── .env                         ✓ VITE_API_URL=http://localhost:5062
└── package.json                 ✓
```

**`AgentBuilder.tsx` — MCP server config (Docker support):**
- Supports `McpToolBinding`: `name`, `command` (e.g. `docker`), `args[]`, `env{}`, `endpoint`, `transport`
- Structured mode: individual fields with add/remove for args and env vars
- JSON paste mode: accepts inner `{ "command": ..., "args": [...], "env": {...} }` object per server
- `ImportJsonPanel`: full Claude Desktop format paste (`{ "mcpServers": { ... } }`)
- **`DockerGatewayPanel`** (new): auto-detects Docker MCP Gateway, test connection, auto-fill binding

**`DockerGatewayPanel` — Docker MCP Gateway auto-detect:**
- Two modes:
  - **stdio** (default): shows `docker mcp gateway run` command; binding uses `command: "docker", args: ["mcp", "gateway", "run"]`
  - **HTTP/SSE**: editable port field (default `8811`); binding uses `endpoint: "http://localhost:{port}/sse"`
- "Test Connection" button → calls `api.probeMcp()` → on success shows discovered tools as green badges; on failure shows error
- "✓ Use this gateway" button (shown only after successful test) → replaces all bindings with the gateway binding via `onUse(binding)` callback
- `hasBindings` guard: a binding is considered valid only when `name.trim() !== ""` AND (`command.trim() !== ""` OR `endpoint.trim() !== ""`); prevents garbage default bindings from being saved

**`AgentChat.tsx` — session management:**
- Stores `sessionId` in component state after first response
- Passes `sessionId` back on every subsequent call
- Displays `Session: xxxxxxxx…` in header
- "Clear" button resets both message list and session ID

**`api.ts` — `AgentResponse` type:**
```typescript
export interface AgentResponse {
  success: boolean;
  content?: string;
  errorMessage?: string;
  agentName?: string;
  sessionId?: string;        // ← added for session continuity
  toolsUsed?: string[];
  executionTime?: string;
  verification?: VerificationResult;  // ← Phase 13
  followUpQuestions?: FollowUpQuestion[];
}
```

**`api.ts` — MCP probe types and method:**
```typescript
export interface McpToolInfo {
  name: string;
  description: string;
}

export interface McpProbeResult {
  success: boolean;
  tools: McpToolInfo[];
  error?: string;
}

// In api object:
probeMcp: (opts: { endpoint?: string; command?: string; args?: string[] }) =>
  request<McpProbeResult>("/api/agents/mcp-probe", { method: "POST", body: JSON.stringify(opts) }),
```

**`AgentChat.tsx` — auto-expand iteration trace:**
- `autoExpandRef = useRef<number | null>(null)` tracks the index of the pending message
- After streaming completes, `setMessages` callback sets `autoExpandRef.current = m.length`
- A `useEffect([messages])` reads the ref and calls `setExpandedIterations(prev => new Set([...prev, idx]))` to auto-open the accordion
- Meta row shows `⚙️ No tools called` (with tooltip) when iterations exist but no MCP tools were invoked — distinguishes "agent ran but called no tools" from "no iterations ran"

**Still pending from original plan:**
- Auth / login flow
- TanStack Query, shadcn/ui, Tailwind migration (optional — current inline approach is functional)
