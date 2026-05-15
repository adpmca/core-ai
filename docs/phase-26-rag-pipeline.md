# Phase 26 — AI-Native RAG Pipeline (Knowledge + Retrieval Layers)

> **Status:** `[ ]` Not Started
> **Depends on:** [phase-04-database.md](phase-04-database.md), [phase-05-mcp-tools.md](phase-05-mcp-tools.md), [phase-08-agents.md](phase-08-agents.md), [phase-19-coordinator-sub-agent-routing.md](phase-19-coordinator-sub-agent-routing.md)
> **Blocks:** Phase 27 (Dev Workflow Agents via 26.5), Phase 28 (Business Workflow Agents via 26.6)
> **Projects:** `Diva.Rag` (new), `Diva.Core`, `Diva.Infrastructure`, `Diva.Tools`, `Diva.Agents`, `Diva.Host`, `admin-portal`
> **Sub-phases:** 26.1 Foundation → 26.2 Agent Memory → 26.3 Enterprise Connectors → 26.4 Advanced Retrieval → 26.5 Dev Workflow Agents → 26.6 Business Workflow Agents → 26.7 Quality & Scale
> **Estimated test count:** ~120 new tests across all sub-phases (~25 per sub-phase 26.1–26.4; ~20 each for 26.5–26.7)

## Context

The platform currently grounds agent responses in session history, business rules, and live MCP tool calls. There is no mechanism for agents to retrieve knowledge from enterprise knowledge bases (Confluence docs, Jira tickets, code repositories, SQL schemas, file stores).

This phase builds **Layers 1–2 of the AI Engineering Ecosystem** described in `docs/arch-overview.md`:

- **Knowledge Layer:** Ingest enterprise content from Confluence, Jira, GitLab, SQL Server, SharePoint/file stores into a versioned vector database (Qdrant)
- **Retrieval Layer:** 4-stage RAG funnel (embedding → metadata filter → hybrid vector search → LLM reranker) surfaced to agents via `search_knowledge` MCP tool and a new supervisor pipeline stage

**Target stack alignment:**

| Enterprise Source | Connector | ExternalVersion Signal |
|-------------------|-----------|----------------------|
| Confluence | `ConfluenceDocumentConnector` | `page.version.number` (integer) |
| Jira | `JiraDocumentConnector` | `issue.fields.updated` (ISO-8601) |
| GitLab | `GitLabDocumentConnector` | file commit SHA |
| SQL Server | `SqlServerSchemaConnector` | `sys.objects.modify_date` |
| SharePoint / file share | `DocumentStoreConnector` | ETag / `LastWriteTimeUtc:Length` |
| Local files | `FileDocumentConnector` | `LastWriteTimeUtc:Length` |
| HTTP/web | `HttpDocumentConnector` | ETag / response SHA-256 |

---

## Sub-Phase Overview

Each sub-phase requires a separate plan approval before implementation begins. Weeks are estimates assuming one developer.

| Sub-phase | Deliverable | Weeks |
|-----------|-------------|-------|
| **26.1 — RAG Foundation** | File ingestion → `search_knowledge` MCP tool → agent-scoped index → supervisor injection | 1–3 |
| **26.2 — Agent Memory** | Agents save/recall vector state across session turns; `summarize_and_archive` | 4–5 |
| **26.3 — Enterprise Connectors** | Confluence, Jira, GitLab, SQL Server, SharePoint, HTTP; webhooks; PII scrubbing | 6–9 |
| **26.4 — Advanced Retrieval** | Hybrid BM25+dense search; entity linking; multi-hop; LLM reranker; full scope hierarchy | 10–12 |
| **26.5 — Dev Workflow Agents** | AI-driven SDLC: Jira → architecture → code → MR → docs via GitLab write API | 13–18 |
| **26.6 — Business Workflow Agents** | IT support, incident response, compliance, project status agents | 19–22 |
| **26.7 — Quality & Scale** | Eval metrics, cost controls, Ollama embeddings, memory quality scoring, Qdrant maintenance | 23–25 |

---

## Architecture

```
INGESTION PIPELINE
──────────────────
[Confluence] [Jira] [GitLab] [SQL Server] [SharePoint] [Files] [HTTP]
         │
    IDocumentConnector
         │
  IDocumentChunker          ← RecursiveTextChunker (512 tok) | CodeChunker (768 tok)
         │
  IMetadataEnricher         ← applies org-structure taxonomy (domain/module/product/contentType)
         │
  IContentScrubber          ← PII/secret redaction before embedding
         │
  IChunkVersioner           ← 3-tier diff: ExternalVersion → ContentHash → ChunkHash
         │ (only changed chunks proceed)
  IEmbeddingService         ← OpenAI text-embedding-3-small; batched; rate-limited
         │
  IVectorRepository         ← Qdrant: upsert point | markStale (is_stale=true) | delete
         │
  KnowledgeChunkEntity      ← SQLite: VectorId, ChunkHash, EntityLinksJson, IsPinned

RETRIEVAL PIPELINE  (PDF "RAG Guardrail Funnel")
────────────────────────────────────────────────
[User Query]
     │
  Stage 1: embed query (IEmbeddingService)
     │
  Stage 2: metadata filter  ← domain/module/product/security + scope (tenant|group|platform)
     │
  Stage 3: Qdrant hybrid search  ← dense cosine + BM25 sparse (FastEmbed server-side), top-20
     │
  Stage 4: LlmReranker  ← top-20 → top-5 (falls back to score order on failure)
     + pinned chunks always prepended (IsPinned=true, up to MaxPinnedChunks=3)
     │
  IContextAssembler  → "Source: {title}\n{text}\n---" per chunk
     │
     ├── search_knowledge MCP tool (agents call during ReAct)
     └── RagContextStage (supervisor: pre-fetch before DecomposeStage)
```

### Knowledge Scope Hierarchy

Mirrors the existing Platform → Group → Tenant cascade for LLM configs and business rules:

| ScopeType | TenantId | GroupId | Managed by | Visible to |
|-----------|----------|---------|------------|------------|
| `platform` | 0 | null | Master admin | All tenants |
| `group` | 0 | group.Id | Group admin | Tenants in group |
| `tenant` | tenantId | null | Tenant admin | That tenant only |

Qdrant retrieval filter combines all applicable scopes via `should` (OR) over tenant + groups + platform, resolved at query time from `IGroupMembershipCache`.

### Per-Datasource Version Strategy (3-Tier Diff)

| Tier | Check | Cost |
|------|-------|------|
| 1. ExternalVersion fast-path | Compare source-system version vs stored — skip if equal | Zero API cost |
| 2. Content hash check | If ExternalVersion changed, fetch content, SHA-256 full text, compare ContentHash | Fetch only |
| 3. Chunk-level hash diff | If content changed, compare each ChunkHash — re-embed only changed chunks | Proportional to changes |

---

## New Project: `src/Diva.Rag/`

**Dependency chain:** `Core → Infrastructure → Rag → Tools → TenantAdmin → Agents → Host`

```
Diva.Rag.csproj                       refs: Diva.Core, Diva.Infrastructure, Qdrant.Client, HtmlAgilityPack, LibGit2Sharp
Abstractions/
  IDocumentConnector.cs               ConnectAsync(source, ct) → IAsyncEnumerable<RawDocument>
  IDocumentChunker.cs                 ChunkAsync(doc) → IReadOnlyList<DocumentChunk>
  IMetadataEnricher.cs                EnrichAsync(chunk, taxonomy) → DocumentChunk
  IContentScrubber.cs                 ScrubAsync(text) → string  (PII/secret redaction)
  IEmbeddingService.cs                EmbedAsync / EmbedBatchAsync + int Dimensions
  IVectorRepository.cs                UpsertAsync / SearchAsync / MarkStaleAsync / DeleteByDocumentAsync
  IKnowledgeRetriever.cs              RetrieveAsync(query, tenantId, filter?, groupIds?) → RetrievalResult
  IDocumentIngestionPipeline.cs       IngestAsync(sourceId, tenantId, documentUri?, ct)
  IContextAssembler.cs                AssembleAsync(chunks[]) → string
Models/
  RawDocument.cs                      DocumentId, SourceId, Title, Uri, Content, ExternalVersion, Metadata
  DocumentChunk.cs                    DocumentId, ChunkIndex, Text, TokenCount, Hash, EntityLinks, Metadata
  ChunkVector.cs                      DocumentChunk + float[] Dense + SparseVector? Sparse
  MetadataTaxonomy.cs                 Domain, Product, Module, ContentType, SecurityLevel, Owner, CustomTagsJson
  KnowledgeFilter.cs                  Domains[], Products[], Modules[], ContentTypes[], SecurityLevels[], SourceIds[]
  RetrievalResult.cs                  IReadOnlyList<RetrievedChunk>, string AssembledContext
  RetrievedChunk.cs                   ChunkId, DocumentId, DocumentTitle, SourceUri, Text, Score, Tags, ScopeType
  EntityLink.cs                       Type (jira_issue|confluence_page|gitlab_mr|sql_table|code_symbol), Id
Connectors/
  FileDocumentConnector.cs            walks allowed paths; reuses IPdfReader/IOfficeReader via DI
  HttpDocumentConnector.cs            GET + HtmlAgilityPack; ETag as ExternalVersion
  ConfluenceDocumentConnector.cs      Atlassian REST API v2; space traversal; label→tag mapping
  ConfluenceStorageFormatParser.cs    Confluence XML storage format → Markdown text
  JiraDocumentConnector.cs            Jira REST API v3; JQL; issue+comments→text; updated as ExternalVersion
  GitLabDocumentConnector.cs          GitLab REST API v4; incremental diff via commits since lastIndexedAt; MR descriptions
  SqlServerSchemaConnector.cs         INFORMATION_SCHEMA + sys.objects; one doc per table/SP/view; FK→entity links; Windows Auth support
  DocumentStoreConnector.cs           SharePoint REST API + Windows UNC share; reuses IOfficeReader/IPdfReader
Linking/
  EntityLinkExtractor.cs              regex pipeline: Jira keys, Confluence IDs, GitLab MR refs, table names, class names
  MultiHopRetriever.cs                follow EntityLinksJson N hops; merge secondary chunks into retrieval result
Chunking/
  RecursiveTextChunker.cs             512 tokens, 50-token overlap; splits at ## boundaries first
  CodeChunker.cs                      768 tokens; splits at class/method boundaries; symbol name metadata
  ChunkingOptions.cs
Embeddings/
  OpenAiEmbeddingService.cs           POST /v1/embeddings via IHttpClientFactory; batch up to 100; token-bucket rate limiter
  OllamaEmbeddingService.cs           local endpoint; for air-gapped deployments
  EmbeddingServiceFactory.cs          selects provider from RagOptions.EmbeddingProvider
Scrubbing/
  PatternContentScrubber.cs           redacts: sk-*, AKIA*, ghp_*, email patterns, credit card patterns; configurable
VectorStore/
  QdrantVectorRepository.cs           Qdrant.Client gRPC wrapper: upsert/search/markStale/delete
  QdrantCollectionManager.cs          EnsureCollectionAsync + payload index creation (idempotent)
  VectorSearchOptions.cs              TopK, ScoreThreshold, Filter, UseHybrid, MinScore
Retrieval/
  KnowledgeRetriever.cs               embed → metadata filter → Qdrant search → rerank → assemble
  LlmReranker.cs                      top-20 candidates + query → LLM → ranked JSON → top-5; fallback to score order
  MetadataFilterBuilder.cs            builds Qdrant must/should filter from KnowledgeFilter + scope
  ContextAssembler.cs                 "Source: {title}\n{text}\n---" per chunk
Ingestion/
  DocumentIngestionPipeline.cs        orchestrates: connect → chunk → enrich → scrub → 3-tier diff → embed → upsert
  IngestionProgressEvent.cs           { Phase, DocumentsProcessed, TotalDocuments, ChunksAdded, ChunksSkipped, Error }
  IngestionWorkerService.cs           IHostedService: polls IngestionJobEntity; SemaphoreSlim concurrency; CancellationToken per job
Services/
  KnowledgeSourceService.cs          CRUD for KnowledgeSourceEntity; cache invalidation; cost estimation
  KnowledgeDocumentService.cs        manages KnowledgeDocumentEntity versions; hash-diff logic; stale marking
RagServiceCollectionExtensions.cs    builder.Services.AddRagPipeline(config)
```

---

## DB Entities (new — `src/Diva.Infrastructure/Data/Entities/`)

All implement `ITenantEntity` unless noted. Added to DivaDbContext with HasQueryFilter + indexes.

### `KnowledgeSourceEntity`
```
Id (string, Guid)           TenantId (int)              Name (string)
ScopeType (string)          → "tenant"|"group"|"platform"
GroupId (string?)           → FK to TenantGroupEntity for group scope; null otherwise
SourceType (string)         → "File"|"Http"|"Confluence"|"Jira"|"GitLab"|"SqlServer"|"SharePoint"
ConfigJson (string)         → source-type-specific config (paths, URLs, spaces, CredentialRef, etc.)
TaxonomyJson (string)       → MetadataTaxonomy JSON: domain, product, module, contentType, securityLevel, owner
Status (string)             → "Active"|"Paused"
WebhookSecretHash (string?) → HMAC-SHA256 of webhook secret for Confluence/GitLab/Jira webhooks
LastIngestedAt (DateTime?)  DocumentCount (int)     ChunkCount (int)
ScheduleEnabled (bool)      ScheduleCron (string?)  → e.g. "0 2 * * *"
CreatedAt (DateTime)        UpdatedAt (DateTime?)
```
`TenantId = 0` for group/platform scope. Index: `(ScopeType, GroupId)`, `(ScopeType, TenantId)`.

### `KnowledgeDocumentEntity`
Stable identity across all re-indexes of the same source document.
```
DocumentId (string)         → stable: Confluence pageId, GitLab file path hash, Jira issue key, SQL object name
TenantId (int)              SourceId (string FK→KnowledgeSourceEntity)
Title (string)              Uri (string)            IsActive (bool)
CurrentVersion (int)        → latest version number (matches newest KnowledgeDocumentVersionEntity)
ExternalVersion (string?)   → latest from source system
LastModifiedAt (DateTime)   LastIndexedAt (DateTime)    CreatedAt (DateTime)
```
Unique index: `(TenantId, SourceId, DocumentId)`.

### `KnowledgeDocumentVersionEntity`
Immutable snapshot per re-index. Modeled after `AgentPromptHistoryEntity`.
```
Id (int, auto)              TenantId (int)
DocumentId (string FK→KnowledgeDocumentEntity)
VersionNumber (int)         → auto-increment per document
ContentHash (string)        → SHA-256 of full document text
ExternalVersion (string?)   Source (string) → "initial_ingest"|"webhook_update"|"manual_sync"|"scheduled_sync"
ChunksAdded (int)           ChunksUpdated (int)     ChunksRemoved (int)
CreatedAt (DateTime)
```
Unique index: `(TenantId, DocumentId, VersionNumber)`.

### `KnowledgeChunkEntity`
Per-vector chunk tracking.
```
Id (string, Guid)           TenantId (int)
DocumentId (string FK→KnowledgeDocumentEntity)
DocumentVersion (int)       ChunkIndex (int)        ChunkHash (string)
VectorId (string)           → Qdrant point ID
TokenCount (int)            IsStale (bool)          IndexedAt (DateTime)
EntityLinksJson (string?)   → [{type, id}] cross-system entity references extracted from chunk text
IsPinned (bool)             PinPriority (int)       → pinned chunks always prepended in retrieval results
```

### `IngestionJobEntity`
```
Id (string, Guid)           TenantId (int)
SourceId (string FK→KnowledgeSourceEntity)
DocumentUri (string?)       → null = full source; non-null = single-document webhook re-index
Status (string)             → "Pending"|"Running"|"Completed"|"Failed"|"Canceled"
DocumentsProcessed (int)    ChunksAdded (int)       ChunksUpdated (int)     ChunksSkipped (int)
ErrorMessage (string?)      TriggerType (string)    → "manual"|"scheduled"|"webhook"
StartedAt (DateTime)        CompletedAt (DateTime?)
```

### `WebhookEventEntity`
```
Id (string, Guid)           TenantId (int)
SourceId (string FK→KnowledgeSourceEntity)
EventType (string)          → "page_created"|"page_updated"|"page_deleted"|"push"|"mr_merged"|"issue_updated"
ExternalId (string)         → Confluence pageId / GitLab commit SHA / Jira issue key
PayloadJson (string)        Status (string) → "Queued"|"Processed"|"Failed"
ReceivedAt (DateTime)       ProcessedAt (DateTime?)     ErrorMessage (string?)
```

### `AgentDefinitionEntity` change
Add field: `KnowledgeProfileJson (string?)` — JSON defining the agent's default knowledge filter and retrieval behaviour. Overrides platform-level default profile for the agent's `ArchetypeId`.

---

## Configuration: `src/Diva.Core/Configuration/RagOptions.cs`

```csharp
public sealed class RagOptions
{
    public const string SectionName = "RAG";
    public bool Enabled { get; set; } = false;
    public string QdrantUrl { get; set; } = "http://qdrant:6333";
    public string? QdrantApiKey { get; set; }
    public string QdrantGrpcUrl { get; set; } = "http://qdrant:6334";
    public string CollectionName { get; set; } = "diva_knowledge";
    public string EmbeddingProvider { get; set; } = "OpenAI";   // "OpenAI" | "Ollama"
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public string? EmbeddingApiKey { get; set; }
    public string? EmbeddingEndpoint { get; set; }              // Ollama: http://localhost:11434
    public int EmbeddingDimensions { get; set; } = 1536;
    public int EmbeddingBatchSize { get; set; } = 100;
    public int MaxEmbeddingBatchesPerJob { get; set; } = 0;     // 0 = unlimited; circuit breaker
    public int DefaultChunkSize { get; set; } = 512;
    public int DefaultChunkOverlap { get; set; } = 50;
    public int DefaultMaxResults { get; set; } = 5;
    public int RerankerCandidates { get; set; } = 20;
    public float MinScoreThreshold { get; set; } = 0.45f;
    public bool EnableHybridSearch { get; set; } = false;       // Phase 25.1: false; Phase 25.3: true
    public bool EnableReranking { get; set; } = true;
    public int MaxPinnedChunks { get; set; } = 3;
    public int MaxConcurrentIngestionJobs { get; set; } = 3;
    public int IngestionPollIntervalSeconds { get; set; } = 5;
    public int AgentFileMaxSizeKb { get; set; } = 1024;             // index_file_to_knowledge size guard
    public int AgentIndexingQuotaChunksPerDay { get; set; } = 0;   // 0 = unlimited; per-agent daily limit
    // Agent Memory
    public bool EnableAgentMemory { get; set; } = false;
    public string MemoryCollectionName { get; set; } = "diva_agent_memory";
    public int WorkingMemoryDefaultTtlMinutes { get; set; } = 480; // 8 hours
    public int EpisodicMemoryDefaultTtlDays { get; set; } = 90;   // 0 = permanent
    public int MemoryCleanupIntervalMinutes { get; set; } = 60;
}
```

`appsettings.json` addition:
```json
"RAG": {
  "Enabled": false,
  "QdrantUrl": "http://qdrant:6333",
  "QdrantGrpcUrl": "http://qdrant:6334",
  "EmbeddingProvider": "OpenAI",
  "EmbeddingModel": "text-embedding-3-small",
  "CollectionName": "diva_knowledge"
}
```

---

## Agent Knowledge Orchestration

### How RAG Attaches to Agents

Two independent paths, both active simultaneously:

| | Path 1 (Embedded MCP) | Path 2 (Supervisor Pipeline) |
|---|---|---|
| **Who calls it** | The agent itself during ReAct | The supervisor pre-fetches automatically |
| **Opt-in** | Agent needs `/mcp/diva` in ToolBindings | `KnowledgeProfileJson.autoInjectContext = true` (default **false**) |
| **Timing** | During agent ReAct tool call | Before `DecomposeStage` runs |
| **Best for** | Dynamic mid-conversation retrieval | Routing decisions, grounding verification |

**Path 1 registration** (`Program.cs`): `.WithDivaMcpTools<KnowledgeRetrievalMcpTools>()` — adds `search_knowledge`, `list_knowledge_sources`, `get_document`, `index_file_to_knowledge` to the embedded `/mcp/diva` server.

**Path 2 guard** (`RagContextStage.ExecuteAsync`): reads `state.SupervisorDefinition.KnowledgeProfileJson.AutoInjectContext`; returns `state` unchanged if `false` (default). Zero cost.

**Direct-agent variant** (`autoQuery`): `AnthropicAgentRunner` checks `definition.KnowledgeProfileJson.AutoQuery`; if `true`, pre-fetches before first LLM call and injects as `## Knowledge Context` in dynamic system prompt.

### `AgentKnowledgeProfile`

Each agent type declares its default knowledge filter as `KnowledgeProfileJson` on `AgentDefinitionEntity`. Platform-level default profiles keyed by `ArchetypeId` (which already exists on the entity — examples: `"rag"`, `"code-analyst"`).

```json
{
  "autoInjectContext": false,
  "autoQuery": false,
  "includeAgentSources": true,
  "includeGroupSources": true,
  "includePlatformSources": true,
  "domains": ["engineering"],
  "products": ["CRM", "ERP"],
  "modules": ["authentication", "payments"],
  "contentTypes": ["code-file", "adr", "confluence-page", "jira-story", "sql-schema"],
  "securityLevels": ["internal", "confidential"],
  "sourceTypes": ["GitLab", "Confluence", "Jira", "SqlServer"],
  "maxResults": 8,
  "entityLinkHops": 1
}
```

`KnowledgeRetrievalMcpTools` reads `ctx.AgentId` → loads `KnowledgeProfileJson` → merges with explicit filter params (explicit wins) → applies to Qdrant query.

### Agent-Scoped Knowledge Index (4th Scope Level)

Agents can maintain a **private vector namespace** in Qdrant. Two entry points:

**A. `index_file_to_knowledge` MCP tool (dynamic, during ReAct):**
```csharp
[McpServerTool(Name = "index_file_to_knowledge")]
// Reads a file via IFileSystemPathGuard + existing readers (PDF, Office, text),
// chunks, embeds, and stores in this agent's private Qdrant namespace.
// Deduplicates by file path. Agent can then retrieve via search_knowledge.
public Task<string> IndexFileToKnowledgeAsync(
    string filePath, string? domain = null, string? module = null, string? contentType = null)
```
Flow: `IFileSystemPathGuard` → `IPdfReader`/`IOfficeReader`/text → `DocumentIngestionPipeline` → Qdrant upsert with `scope_type = "agent"`, `agent_id = ctx.AgentId`. Returns `{ chunksIndexed, documentId, status }` synchronously (no SSE — single file is fast).

**B. Admin pre-seeded agent sources** — admin creates `KnowledgeSourceEntity` with `ScopeType = "agent"`, `AgentId = targetAgentId` via AgentBuilder Knowledge Profile tab.

**Schema changes for agent scope:**

`KnowledgeSourceEntity` — new field: `AgentId (string?)` — FK to `AgentDefinitionEntity.Id`; null for tenant/group/platform.

Qdrant payload — new field: `"agent_id": "agent-guid-or-null"`. Payload index added by `QdrantCollectionManager`.

`MetadataFilterBuilder` extends the `should` (OR) filter to include an agent-scoped clause when `ctx.AgentId != null && profile.IncludeAgentSources`:
```json
{ "must": [
    { "key": "scope_type", "match": { "value": "agent" } },
    { "key": "agent_id",   "match": { "value": "<agentId>" } }
]}
```
Agent-scoped chunks are automatically included in every `search_knowledge` call made by that agent — no extra filter param needed.

### `X-Agent-Id` Header Propagation

**Modified:** `src/Diva.Infrastructure/LiteLLM/AnthropicAgentRunner.cs` — inject `X-Agent-Id: {definition.Id}` into all outbound MCP tool HTTP requests (alongside existing tenant headers).

**Modified:** `src/Diva.Tools/Core/McpServerContext.cs` — add `AgentId` property extracted from `X-Agent-Id` header.

### Entity Linking (Lightweight Knowledge Graph)

During ingestion, `EntityLinkExtractor` scans each chunk for cross-system references:

| Pattern | Match | Link type |
|---------|-------|-----------|
| Jira issue key | `[A-Z]+-\d+` e.g. `PLAT-1234` | `jira_issue` |
| Confluence page URL | `/pages/\d+` | `confluence_page` |
| GitLab MR reference | `!\d+` | `gitlab_mr` |
| SQL table (EF DbSet / FROM clause) | known table names from schema index | `sql_table` |
| C# class name | `\b[A-Z][a-zA-Z]+Service\b` etc. | `code_symbol` |
| Git commit SHA | `[0-9a-f]{7,40}` | `git_commit` |

Stored in `KnowledgeChunkEntity.EntityLinksJson` and mirrored to Qdrant payload `entity_links[]`.

`MultiHopRetriever` (Phase 25.3): primary search → extract entity IDs from top-5 → Qdrant payload filter for linked chunks → merge into context (lower reranker weight).

---

## Agent Memory (Vector State)

Agents can persist and semantically retrieve their own state in Qdrant — distinct from the enterprise RAG pipeline. RAG is admin-ingested read-only knowledge; agent memory is agent-written, session or agent scoped, and self-managed.

### Memory Tiers

| Type | Scope | TTL | Use case |
|------|-------|-----|---------|
| `working` | Session | Expires at session end (or explicit `expiresIn`) | Current task facts, intermediate results, discovered values |
| `episodic` | Agent | Long-term (months, configurable) | Past task outcomes, patterns learned across sessions |
| `semantic` | Agent | Permanent (until explicitly cleared) | LLM-condensed summaries distilled from many sessions |

### DB Entity: `AgentMemoryEntity`

New entity in `src/Diva.Infrastructure/Data/Entities/AgentMemoryEntity.cs`. Implements `ITenantEntity`.

```
Id (string, Guid)           TenantId (int)
AgentId (string FK→AgentDefinitionEntity)
SessionId (string?)         → null for episodic/semantic; FK to AgentSessionEntity for working
MemoryType (string)         → "working"|"episodic"|"semantic"
Content (string)            → original text (audit trail + fallback if Qdrant unavailable)
VectorId (string)           → Qdrant point ID (in diva_agent_memory collection)
TagsJson (string?)          → JSON string[] of semantic tags for filtering
ExpiresAt (DateTime?)       → null = permanent; set for working memories
CreatedAt (DateTime)
```

Index: `(TenantId, AgentId, MemoryType)`, `(TenantId, SessionId)`, `(ExpiresAt)` for cleanup queries.

### Qdrant Collection: `diva_agent_memory`

Separate collection from `diva_knowledge` — same embedding dimensions, same gRPC channel, different namespace.

**Payload schema per memory point:**
```json
{
  "scope_type": "agent_memory",
  "tenant_id": 1,
  "agent_id": "agent-guid",
  "session_id": "session-guid-or-null",
  "memory_type": "working",
  "content": "Discovered that table dbo.Orders has 2.4M rows and no archiving policy",
  "tags": ["sql", "performance", "orders"],
  "expires_at": "2026-05-15T00:00:00Z",
  "created_at": "2026-05-14T10:00:00Z"
}
```

Payload indexes: `tenant_id`, `agent_id`, `session_id`, `memory_type`, `expires_at`.

Created by `QdrantCollectionManager.EnsureMemoryCollectionAsync` (idempotent, called at startup alongside `EnsureCollectionAsync` for knowledge).

### New MCP Tools (added to `KnowledgeRetrievalMcpTools`)

```csharp
[McpServerTool(Name = "save_memory")]
// Embeds content and stores it in this agent's memory.
// memoryType: "working" (expires at session end), "episodic" (long-term), "semantic" (permanent)
// tags: optional semantic labels for filtering on recall
// expiresInMinutes: override TTL for working memories (default: session end)
public Task<string> SaveMemoryAsync(
    string content, string memoryType = "working",
    string[]? tags = null, int? expiresInMinutes = null)

[McpServerTool(Name = "recall_memory")]
// Semantic search over this agent's stored memories.
// memoryType: filter to specific tier; null = all types
// sessionId: restrict to current session (working memory) or null for cross-session
// maxResults: 1-10 (default 5)
public Task<string> RecallMemoryAsync(
    string query, string? memoryType = null,
    bool currentSessionOnly = false, int maxResults = 5)

[McpServerTool(Name = "forget_memory")]
// Delete a specific memory by its ID (returned by save_memory).
// Use to correct outdated facts or clear sensitive information.
public Task<string> ForgetMemoryAsync(string memoryId)

[McpServerTool(Name = "summarize_and_archive")]
// Condenses all working memories for the current session into a single episodic memory
// using the LLM, then deletes the working memories. Call at end of long-running tasks
// to keep memory clean and distil learnings into agent's long-term memory.
public Task<string> SummarizeAndArchiveAsync(string? archiveTitle = null)
```

### Memory Cleanup Service

`MemoryCleanupService` (new `IHostedService`) runs on a configurable interval. Queries `AgentMemoryEntity WHERE ExpiresAt < UtcNow`, batches Qdrant deletes by `VectorId`, then deletes DB rows. Also triggered at session close by hooking into `AgentSessionService.CloseSessionAsync`.

**`RagOptions` additions:**
```csharp
public bool EnableAgentMemory { get; set; } = false;
public string MemoryCollectionName { get; set; } = "diva_agent_memory";
public int WorkingMemoryDefaultTtlMinutes { get; set; } = 480;    // 8 hours
public int EpisodicMemoryDefaultTtlDays { get; set; } = 90;       // 3 months; 0 = permanent
public int MemoryCleanupIntervalMinutes { get; set; } = 60;
```

### `KnowledgeProfileJson` additions for memory

```json
{
  "enableMemory": false,
  "memoryAutoRecall": false,
  "memoryAutoRecallTypes": ["episodic", "semantic"],
  "memoryMaxRecallResults": 3,
  ...existing fields...
}
```

`memoryAutoRecall = true`: before the first ReAct LLM call, `AnthropicAgentRunner` calls `RecallMemoryAsync(request.Query, memoryType=episodic/semantic)` and prepends results as `## Agent Memory` block in the dynamic system prompt — giving the agent awareness of past experience without it explicitly calling the tool.

### UI: `AgentMemory.tsx` (new admin page)

```
admin-portal/src/pages/rag/AgentMemory.tsx
  Filter: agent picker, memory type tabs (Working | Episodic | Semantic)
  Table: content preview, tags, created at, expires at, session ID
  Actions: "Forget" (delete single), "Archive Session" (trigger summarize_and_archive), "Clear All" per type
  Read-only for working memories owned by active sessions
```

**`AgentBuilder.tsx` Knowledge Profile tab additions:**
- `enableMemory (toggle, default OFF)` — "Allow agent to save/recall memories"
- `memoryAutoRecall (toggle, default OFF)` — "Auto-inject past memories before first response"
- `memoryAutoRecallTypes (multi-select: working|episodic|semantic)` — types to include in auto-recall

### Implementation note

Memory tools reuse `IEmbeddingService` and `IVectorRepository` already wired up for the knowledge pipeline. `IVectorRepository` gains a `MemoryCollectionName` parameter override (or a second `IVectorRepository` registration keyed to the memory collection). `IAgentMemoryService` (new service in `Diva.Rag/Services/`) wraps the save/recall/forget lifecycle and handles TTL assignment.

---

## Qdrant Payload Schema

One payload document per vector point (knowledge collection):

```json
{
  "scope_type": "tenant",
  "scope_id": 1,
  "source_id": "guid",
  "document_id": "PLAT-1234",
  "document_version": 3,
  "chunk_index": 0,
  "text": "...",
  "title": "PLAT-1234: Add OAuth2 PKCE flow",
  "domain": "engineering",
  "product": "CRM",
  "module": "authentication",
  "content_type": "jira-story",
  "security_level": "internal",
  "owner": "platform-team",
  "tags": ["oauth2", "pkce", "security"],
  "source_type": "Jira",
  "source_uri": "https://company.atlassian.net/browse/PLAT-1234",
  "external_version": "2026-05-14T08:00:00Z",
  "entity_links": [
    { "type": "confluence_page", "id": "98765" },
    { "type": "sql_table", "id": "dbo.OAuthTokens" }
  ],
  "agent_id": null,
  "is_stale": false,
  "is_pinned": false,
  "indexed_at": "2026-05-14T00:00:00Z",
  "token_count": 256,
  "language": "en"
}
```

**Payload indexes** (created by `QdrantCollectionManager.EnsureCollectionAsync`):
`scope_type`, `scope_id`, `agent_id`, `is_stale`, `is_pinned`, `domain`, `product`, `module`, `content_type`, `security_level`, `source_id`, `document_id`, `entity_links`

---

## MCP Tool Server: `src/Diva.Tools/Rag/KnowledgeRetrievalMcpTools.cs`

```csharp
[McpServerToolType]
public sealed class KnowledgeRetrievalMcpTools(
    IKnowledgeRetriever retriever,
    IKnowledgeSourceService sourceService,
    IAgentDefinitionRepository agentRepo,
    IAgentMemoryService memoryService,           // agent memory read/write
    IDocumentIngestionPipeline ingestionPipeline, // index_file_to_knowledge
    IFileSystemPathGuard pathGuard,              // path validation
    IPdfReader pdfReader,
    IOfficeReader officeReader,
    IOptions<RagOptions> ragOptions,
    IHttpContextAccessor http,
    ILogger<KnowledgeRetrievalMcpTools> logger) : IDivaMcpToolType
{
    // ── Enterprise Knowledge ──────────────────────────────────────────────────

    [McpServerTool(Name = "search_knowledge")]
    // Searches the enterprise knowledge base. Agent's KnowledgeProfile auto-applied as default filter.
    // Includes agent-scoped private chunks automatically when includeAgentSources=true (default).
    // domain/module/contentType: override profile defaults; maxResults: 1-10
    public Task<string> SearchKnowledgeAsync(
        string query, string? domain = null, string? product = null,
        string? module = null, string? contentType = null, int maxResults = 5)

    [McpServerTool(Name = "list_knowledge_sources")]
    // Returns JSON array of all active sources visible to this agent:
    // tenant + group + platform sources + this agent's private agent-scoped sources
    public Task<string> ListKnowledgeSourcesAsync()

    [McpServerTool(Name = "get_document")]
    // Returns assembled context for a specific document by its stable DocumentId
    public Task<string> GetDocumentAsync(string documentId)

    [McpServerTool(Name = "index_file_to_knowledge")]
    // Reads filePath via IFileSystemPathGuard + existing readers (PDF, Office, text),
    // chunks, embeds, and stores in this agent's private Qdrant namespace (scope_type="agent").
    // Deduplicates by path. Agent can then retrieve via search_knowledge.
    // Rejects files > RagOptions.AgentFileMaxSizeKb (default 1 MB).
    public Task<string> IndexFileToKnowledgeAsync(
        string filePath, string? domain = null, string? module = null, string? contentType = null)

    // ── Agent Memory ──────────────────────────────────────────────────────────

    [McpServerTool(Name = "save_memory")]
    // Embeds content and stores it in this agent's memory (diva_agent_memory Qdrant collection).
    // memoryType: "working" (expires at session end by default), "episodic" (long-term), "semantic" (permanent)
    // tags: optional semantic labels for filtering on recall
    // expiresInMinutes: override default TTL for working memories (0 = use RagOptions default)
    // Returns: { "memoryId": "guid", "memoryType": "...", "expiresAt": "..." }
    public Task<string> SaveMemoryAsync(
        string content, string memoryType = "working",
        string[]? tags = null, int expiresInMinutes = 0)

    [McpServerTool(Name = "recall_memory")]
    // Semantic search over this agent's stored memories in diva_agent_memory collection.
    // memoryType: filter to specific tier ("working"|"episodic"|"semantic"); null = all
    // currentSessionOnly: restrict to memories saved in the current session only
    // maxResults: 1-10 (default 5)
    public Task<string> RecallMemoryAsync(
        string query, string? memoryType = null,
        bool currentSessionOnly = false, int maxResults = 5)

    [McpServerTool(Name = "forget_memory")]
    // Delete a specific memory by its ID (returned by save_memory).
    // Use to correct outdated facts or clear sensitive information.
    public Task<string> ForgetMemoryAsync(string memoryId)

    [McpServerTool(Name = "summarize_and_archive")]
    // Condenses all working memories for the current session into a single episodic memory
    // using an LLM call, then deletes the working memories.
    // Call at the end of long-running tasks to distil learnings into long-term memory.
    // archiveTitle: optional label for the resulting episodic memory
    public Task<string> SummarizeAndArchiveAsync(string? archiveTitle = null)
}
```

---

## Supervisor Pipeline Changes

### New Stage: `RagContextStage`

**File:** `src/Diva.Agents/Supervisor/Stages/RagContextStage.cs`

**Registration:** add `builder.Services.AddSingleton<ISupervisorPipelineStage, RagContextStage>()` between `AgentContextStage` and `DecomposeStage` in `Program.cs` (lines 246–247). Stage order is registration order (`IEnumerable<ISupervisorPipelineStage>` injected into `SupervisorAgent`).

**Guard:** only fires when `RagOptions.Enabled = true`.

**Effect:** calls `IKnowledgeRetriever.RetrieveAsync(state.Request.Query, tenantId, groupIds)`, sets `state.RetrievedContext` and `state.RetrievedChunks`.

### `SupervisorState` additions
```csharp
// src/Diva.Agents/Supervisor/SupervisorState.cs
public string? RetrievedContext { get; set; }
public IReadOnlyList<RetrievedChunk>? RetrievedChunks { get; set; }
public AgentDefinitionEntity? SupervisorDefinition { get; set; }   // loaded by SupervisorAgent; read by RagContextStage
```

### Injection points for `state.RetrievedContext`

| Stage | Injection | Purpose |
|-------|-----------|---------|
| `DecomposeStage` | Prepend `## Retrieved Knowledge\n{ctx}` to LLM decomposition prompt | Better routing decisions |
| `DispatchStage` | Append to `AgentRequest.Instructions` per worker | Workers receive relevant context |
| `VerifyStage` | Append to `state.ToolEvidence` | Grounding-based confidence scoring |

---

## REST API: `src/Diva.Host/Controllers/RagController.cs`

```
Knowledge Sources:
  POST   /api/rag/sources                     create source (scope validation in service)
  GET    /api/rag/sources                     list — tenant + group + platform sources visible to caller
  GET    /api/rag/sources/{id}                get source + last job status
  PUT    /api/rag/sources/{id}                update config/taxonomy/schedule
  DELETE /api/rag/sources/{id}                delete + Qdrant cleanup

Ingestion:
  POST   /api/rag/sources/{id}/ingest         trigger job; SSE stream IngestionProgressEvent JSON
  GET    /api/rag/sources/{id}/jobs           list ingestion jobs (pagination)
  GET    /api/rag/jobs/{jobId}                poll single job status
  DELETE /api/rag/jobs/{jobId}                cancel running job
  GET    /api/rag/sources/{id}/estimate       dry-run: returns estimated token count + embedding cost

Documents:
  GET    /api/rag/sources/{id}/documents      list documents (with current version info)
  GET    /api/rag/documents/{docId}/versions  immutable version history

Query / Test:
  POST   /api/rag/query                       test retrieval; returns chunks + context + scores + entity links

Evaluation (Phase 25.6):
  POST   /api/rag/evaluate                    run benchmark queries; store RagEvaluationRunEntity
  GET    /api/rag/evaluate/runs               history of evaluation runs + hit-rate@5 / MRR metrics

Agent Memory (admin):
  GET    /api/rag/agents/{agentId}/memories   list memories (paginated; filter by type/session)
  DELETE /api/rag/memories/{memoryId}         forget a specific memory (admin override)
  DELETE /api/rag/agents/{agentId}/memories   clear all memories for an agent (by type, optional)
  POST   /api/rag/agents/{agentId}/memories/archive  trigger summarize_and_archive for a session

Webhooks (no auth — HMAC validated per source):
  POST   /api/rag/webhooks/confluence         Confluence page_created/updated/deleted
  POST   /api/rag/webhooks/gitlab             GitLab push / MR merged / pipeline failed
  POST   /api/rag/webhooks/jira              Jira issue_updated
  GET    /api/rag/webhooks/events             webhook event audit log (admin-auth)
```

All admin endpoints use `EffectiveTenantId` pattern. Webhook endpoints skip Bearer auth; validate via HMAC-SHA256 against `KnowledgeSourceEntity.WebhookSecretHash`.

---

## Connector Designs

### Confluence Connector Config
```json
{
  "BaseUrl": "https://yourcompany.atlassian.net",
  "SpaceKeys": ["ENG", "ARCH"],
  "AuthType": "ApiToken",
  "CredentialRef": "confluence-api-token",
  "WebhookSecret": "...",
  "IncludePageTypes": ["page", "blogpost"],
  "ExcludeLabels": ["draft", "wip"],
  "MaxDepth": 0
}
```
API: `GET {base}/wiki/rest/api/space/{key}/content?expand=body.storage,version,metadata.labels,ancestors&limit=50`
ExternalVersion: `page.version.number`
Webhook: `X-Hub-Signature: sha256={HMAC-SHA256(body, secret)}` — events: `page_created`, `page_updated`, `page_deleted`

### Jira Connector Config
```json
{
  "BaseUrl": "https://yourcompany.atlassian.net",
  "ProjectKeys": ["PLAT", "ENG"],
  "IssueTypes": ["Story", "Bug", "Epic"],
  "Statuses": ["Done", "In Progress"],
  "IncludeComments": true,
  "MaxIssuesPerSync": 5000,
  "CredentialRef": "jira-api-token"
}
```
API: `GET {base}/rest/api/3/search?jql=project IN (...)&fields=summary,description,issuetype,status,labels,comment,updated`
ExternalVersion: `issue.fields.updated` ISO-8601

### GitLab Connector Config
```json
{
  "BaseUrl": "https://gitlab.yourcompany.com",
  "ProjectIds": [42, 87],
  "Branch": "main",
  "CredentialRef": "gitlab-pat",
  "IncludeExtensions": [".cs", ".ts", ".tsx", ".html", ".scss", ".md", ".yaml"],
  "ExcludePaths": ["**/node_modules/**", "**/bin/**", "**/obj/**"],
  "IndexMRDescriptions": true,
  "LastCommitSha": null
}
```
Incremental: `GET /projects/{id}/repository/commits?ref_name={branch}&since={lastIndexedAt}` → diff changed files only
ExternalVersion: commit SHA of last commit touching each file
Webhook: `X-Gitlab-Token` header validation — events: `push`, `merge_request`, `pipeline`

### SQL Server Connector Config
```json
{
  "ConnectionString": "Server=.;Database=ProductionDb;Integrated Security=true",
  "AuthType": "WindowsIntegrated",
  "IncludeSchemas": ["dbo", "sales"],
  "ExcludeTablePatterns": ["__EF*", "sysdiagram*"],
  "IndexStoredProcedures": true,
  "IndexViews": true
}
```
ExternalVersion: `CONVERT(VARCHAR, OBJECT_MODIFY_DATE, 126)` from `sys.objects`
One chunk per table (columns + FK relationships) + one chunk per SP body

### Document Store Config
```json
{
  "StoreType": "SharePoint",
  "BaseUrl": "https://yourcompany.sharepoint.com/sites/Engineering",
  "IncludePaths": ["/Shared Documents/Architecture"],
  "IncludeExtensions": [".docx", ".xlsx", ".pdf", ".pptx", ".md"],
  "CredentialRef": "sharepoint-app-registration"
}
```
For `StoreType: "WindowsShare"`: `{ "UncPath": "\\\\fileserver\\Docs", "CredentialRef": null }`

---

## Admin Portal (React)

```
admin-portal/src/pages/rag/
  RagSettings.tsx                ← platform-level RAG + Qdrant global configuration (master admin only)
                                   Section: Vector Store
                                     QdrantUrl (text), QdrantGrpcUrl (text), QdrantApiKey (password)
                                     CollectionName (text), "Test Connection" button → ping Qdrant REST health
                                   Section: Embedding
                                     EmbeddingProvider (OpenAI | Ollama), EmbeddingModel (text)
                                     EmbeddingApiKey (password — stored via CredentialRef pattern)
                                     EmbeddingEndpoint (text, shown when Ollama selected)
                                     EmbeddingDimensions (number), EmbeddingBatchSize (number)
                                     MaxEmbeddingBatchesPerJob (number, 0 = unlimited)
                                   Section: Retrieval
                                     DefaultChunkSize (number), DefaultChunkOverlap (number)
                                     DefaultMaxResults (number), RerankerCandidates (number)
                                     MinScoreThreshold (slider 0.0–1.0), MaxPinnedChunks (number)
                                     EnableHybridSearch (toggle — disabled until Phase 25.3)
                                     EnableReranking (toggle)
                                   Section: Ingestion Worker
                                     MaxConcurrentIngestionJobs (number), IngestionPollIntervalSeconds (number)
                                   Saves to DB-backed ObservabilityConfigEntity pattern (or new RagConfigEntity)
                                   — NOT appsettings.json; hot-reload without restart

  KnowledgeSources.tsx           table: name, type, scope badge (Tenant | Group | Platform | Agent), domain,
                                   status (Active|Paused), docs count, chunks count, last ingested
                                 tabs: My Sources (tenant-scoped) | Group Sources | Platform Sources | Agent Sources
                                   → Group Sources tab: only visible if user belongs to ≥1 group
                                   → Platform Sources tab: only visible to master admin (TenantId=0)
                                   → Agent Sources tab: read-only view, grouped by agent name (shows auto-indexed files)
                                 row actions: Ingest Now (→ SSE progress modal), Edit, Pause/Resume, Delete

  KnowledgeSourceEditor.tsx      Step 1 — Scope selector
                                   Tenant (default) | Group (group picker) | Platform (master admin only) | Agent (agent picker — user's agents only)
                                 Step 2 — Source type selector
                                   File | HTTP | Confluence | Jira | GitLab | SQL Server | SharePoint
                                 Step 3 — Connector config subform (dynamic per type):
                                   FileConfigPanel:
                                     BasePaths (multi-line), IncludeExtensions (tag input),
                                     ExcludePaths (tag input), MaxFileSizeKb (number)
                                   HttpConfigPanel:
                                     Url (text), CssSelector (text, optional — extract specific element),
                                     FollowLinks (toggle), MaxDepth (number), CredentialRef (dropdown)
                                   ConfluenceConfigPanel:
                                     BaseUrl (text), SpaceKeys (tag input), AuthType (ApiToken|OAuth2),
                                     CredentialRef (dropdown), IncludePageTypes (checkboxes: page, blogpost),
                                     ExcludeLabels (tag input), MaxDepth (number),
                                     WebhookSecret (password — generates HMAC hash on save),
                                     "Show webhook endpoint URL" info box
                                   JiraConfigPanel:
                                     BaseUrl (text), ProjectKeys (tag input), CredentialRef (dropdown),
                                     IssueTypes (checkboxes: Story, Bug, Epic, Task),
                                     Statuses (tag input), ExcludeLabels (tag input),
                                     IncludeComments (toggle), MaxIssuesPerSync (number),
                                     "Show webhook endpoint URL" info box
                                   GitLabConfigPanel:
                                     BaseUrl (text), ProjectIds (number input with + add), Branch (text),
                                     CredentialRef (dropdown), IndexMode (Files|MRDescriptions|Both),
                                     IncludeExtensions (tag input), ExcludePaths (tag input),
                                     IndexMRDescriptions (toggle), IndexPipelineLogs (toggle),
                                     WebhookSecret (password),
                                     "Show webhook endpoint URL" info box
                                   SqlServerConfigPanel:
                                     AuthType (WindowsIntegrated | SqlLogin),
                                     ConnectionString (text — masked, only shown if SqlLogin),
                                     CredentialRef (dropdown — for SqlLogin credentials),
                                     IncludeSchemas (tag input), ExcludeTablePatterns (tag input),
                                     IndexStoredProcedures (toggle), IndexViews (toggle),
                                     IndexFunctions (toggle)
                                   SharePointConfigPanel:
                                     StoreType (SharePoint | WindowsShare),
                                     BaseUrl / UncPath (text — context-sensitive label),
                                     IncludePaths (multi-line), IncludeExtensions (tag input),
                                     ExcludePatterns (tag input), CredentialRef (dropdown)
                                 Step 4 — MetadataTaxonomyPanel
                                   Domain (dropdown: engineering|finance|hr|operations|product|legal|custom),
                                   Product (text — e.g. CRM, ERP), Module (text),
                                   ContentType (auto-detected, overridable dropdown),
                                   SecurityLevel (public|internal|confidential|restricted),
                                   Owner (text — team or person name),
                                   CustomTags (tag input)
                                 Step 5 — SchedulePanel
                                   ScheduleEnabled (toggle), CronExpression (text with human-readable preview),
                                   "Next run at: {datetime}" display

  IngestionJobs.tsx              job history table: source name, trigger (manual|scheduled|webhook),
                                   status badge, docs processed, chunks added/updated/skipped, elapsed, error
                                   expandable row: IngestionProgressEvent log per job

  KnowledgeExplorer.tsx          query text input + "Search" button
                                 scope toggles: Include Tenant / Include Group / Include Platform / Include Agent (this agent)
                                 filter pills: domain, module, contentType, securityLevel, product
                                 results list: score bar, title, source URI, scope badge, content preview,
                                   entity links (clickable chips), "Pin this chunk" toggle
                                 "Estimate cost" panel: token count + embedding cost before triggering ingest

  DocumentVersions.tsx           version history for a document: version#, trigger source,
                                   content hash (truncated), chunks added/updated/removed, date/time

  WebhookEvents.tsx              audit log: source name, event type, external ID, status badge,
                                   received at, processed at, error message (if failed)
                                   "Replay" action for failed events

  AgentMemory.tsx                agent memory browser (admin only)
                                 Filter: agent picker, memory type tabs (Working | Episodic | Semantic)
                                 Table: content preview, tags, created at, expires at, session ID
                                 Actions: "Forget" (delete single), "Archive Session" (trigger summarize_and_archive),
                                   "Clear All" per type — read-only for working memories in active sessions

admin-portal/src/api/ragApi.ts   typed fetch wrappers for all /api/rag/* endpoints
                                 including RagSettings GET/PUT, estimate, evaluate, agent memory CRUD

AgentBuilder.tsx additions:      new "Knowledge Profile" tab
                                   autoInjectContext (toggle, default OFF) — "Pre-fetch for supervisor routing"
                                   autoQuery (toggle, default OFF) — "Auto-inject before first response"
                                   includeAgentSources (toggle, default ON) — "Include this agent's private files"
                                   includeGroupSources (toggle), includePlatformSources (toggle)
                                   domains (tag input), products (tag input), modules (tag input)
                                   contentTypes (multi-select), securityLevels (multi-select)
                                   sourceTypes (multi-select)
                                   maxResults (slider 1–10), entityLinkHops (0|1|2)
                                   Agent Sources sub-panel: list of pre-seeded agent-scoped sources
                                     + "Add Source" button → opens KnowledgeSourceEditor pre-scoped to this agent
```

### Qdrant Settings REST API (new endpoints in `RagController`)

```
GET    /api/rag/settings         returns current RagOptions (embedding API key masked)
PUT    /api/rag/settings         update + hot-reload (master admin only; persisted to DB)
POST   /api/rag/settings/test    ping Qdrant health + test embedding API key → returns status JSON
```

Settings persisted in a new `RagConfigEntity` (platform-scoped, `TenantId = 0`, no `ITenantEntity`) — same pattern as `ObservabilityConfigEntity`. Hot-reload via `IOptionsMonitor<RagOptions>` sink updated after write.

---

## Docker Compose Addition

```yaml
services:
  qdrant:
    image: qdrant/qdrant:v1.9.0
    ports:
      - "6333:6333"   # REST
      - "6334:6334"   # gRPC
    volumes:
      - qdrant_data:/qdrant/storage
    environment:
      QDRANT__SERVICE__API_KEY: ${QDRANT_API_KEY:-}
    restart: unless-stopped

volumes:
  qdrant_data:
```

`.env.example` additions:
```
QDRANT_API_KEY=
RAG_EMBEDDING_API_KEY=
```

---

## NuGet Packages to Add

| Package | Version | Project |
|---------|---------|---------|
| `Qdrant.Client` | `1.12.*` | `Diva.Rag` (gRPC client) |
| `HtmlAgilityPack` | `1.11.*` | `Diva.Rag` (HTTP connector HTML parsing) |
| `LibGit2Sharp` | `0.31.*` | `Diva.Rag` (local Git diff support) |

Confluence, GitLab, Jira, SharePoint: plain `HttpClient` via `IHttpClientFactory` (no vendor SDK).
SQL Server schema: existing `Microsoft.Data.SqlClient` (transitive via EF SQL Server package).

---

## Files Modified

| File | Change |
|------|--------|
| [Diva.slnx](../Diva.slnx) | Add `src/Diva.Rag/Diva.Rag.csproj` |
| [src/Diva.Infrastructure/Data/DivaDbContext.cs](../src/Diva.Infrastructure/Data/DivaDbContext.cs) | 5 new DbSets + query filters + indexes |
| [src/Diva.Infrastructure/Data/Entities/AgentDefinitionEntity.cs](../src/Diva.Infrastructure/Data/Entities/AgentDefinitionEntity.cs) | Add `KnowledgeProfileJson (string?)` |
| [src/Diva.Agents/Supervisor/SupervisorState.cs](../src/Diva.Agents/Supervisor/SupervisorState.cs) | Add `RetrievedContext`, `RetrievedChunks`, **`SupervisorDefinition AgentDefinitionEntity?`** |
| [src/Diva.Agents/Supervisor/SupervisorAgent.cs](../src/Diva.Agents/Supervisor/SupervisorAgent.cs) | Load reserved supervisor `AgentDefinitionEntity` row by name at startup; populate `state.SupervisorDefinition` in `InvokeAsync` |
| [src/Diva.Agents/Supervisor/Stages/DecomposeStage.cs](../src/Diva.Agents/Supervisor/Stages/DecomposeStage.cs) | Inject RAG context into decomposition prompt |
| [src/Diva.Agents/Supervisor/Stages/DispatchStage.cs](../src/Diva.Agents/Supervisor/Stages/DispatchStage.cs) | Append RAG context to worker `AgentRequest.Instructions` |
| [src/Diva.Agents/Supervisor/Stages/VerifyStage.cs](../src/Diva.Agents/Supervisor/Stages/VerifyStage.cs) | Append to `ToolEvidence` for grounding score |
| [src/Diva.Infrastructure/LiteLLM/AnthropicAgentRunner.cs](../src/Diva.Infrastructure/LiteLLM/AnthropicAgentRunner.cs) | Inject `X-Agent-Id: {definition.Id}` header in all outbound MCP tool HTTP calls; check `KnowledgeProfileJson.AutoQuery` for direct-agent pre-fetch |
| [src/Diva.Tools/Core/McpServerContext.cs](../src/Diva.Tools/Core/McpServerContext.cs) | Add `AgentId (string?)` property extracted from `X-Agent-Id` request header |
| [src/Diva.Host/Program.cs](../src/Diva.Host/Program.cs) | `.WithDivaMcpTools<KnowledgeRetrievalMcpTools>()`, `RagContextStage` (between `AgentContextStage` and `DecomposeStage`), `IngestionWorkerService`, all RAG services |
| [src/Diva.Host/appsettings.json](../src/Diva.Host/appsettings.json) | Add `RAG` config section (Enabled=false by default) |
| [docker-compose.yml](../docker-compose.yml) | Add Qdrant service (REST 6333 + gRPC 6334) + `qdrant_data` volume |
| [admin-portal/src/App.tsx](../admin-portal/src/App.tsx) | Add `/rag/*` routes |
| `src/Diva.Infrastructure/Data/Entities/RagConfigEntity.cs` (new) | Platform-scoped Qdrant + embedding settings; no `ITenantEntity`; singleton row (`TenantId = 0`) — same pattern as `ObservabilityConfigEntity` |
| `src/Diva.Infrastructure/Data/Entities/KnowledgeSourceEntity.cs` (new) | Includes `AgentId (string?)` FK + `ScopeType = "agent"` for 4th scope level; unique index `(TenantId, AgentId, SourceType)` for auto-created agent sources |
| `src/Diva.Infrastructure/Data/Entities/AgentMemoryEntity.cs` (new) | Per-agent/session memory record with `VectorId`, `MemoryType`, `ExpiresAt`; implements `ITenantEntity` |
| `src/Diva.Rag/Services/IAgentMemoryService.cs` + `AgentMemoryService.cs` (new) | Save / recall / forget / summarize-and-archive lifecycle; manages `diva_agent_memory` Qdrant collection via `IVectorRepository` |
| `src/Diva.Rag/Ingestion/MemoryCleanupService.cs` (new) | `IHostedService`: purges expired `AgentMemoryEntity` rows + Qdrant points on configurable interval; also triggered at session close |

---

## Sub-Phase Implementation Plans

---

### Phase 26.1 — RAG Foundation
**Weeks 1–3 | Prerequisite for all subsequent sub-phases**

**Deliverable:** File-based knowledge sources → ingestion pipeline → `search_knowledge` MCP tool works; agent-scoped private index via `index_file_to_knowledge`; supervisor Path 2 injection opt-in.

**Implementation steps:**
1. `Diva.Rag` project skeleton: `Diva.Rag.csproj` + all abstractions (`IDocumentConnector`, `IDocumentChunker`, `IEmbeddingService`, `IVectorRepository`, `IKnowledgeRetriever`, `IDocumentIngestionPipeline`, `IContextAssembler`) + models (`RawDocument`, `DocumentChunk`, `MetadataTaxonomy`, `KnowledgeFilter`, `RetrievalResult`, `EntityLink`)
2. `RecursiveTextChunker` (512 tok, 50 overlap) + `CodeChunker` (768 tok, class/method boundary split) + `ChunkingOptions`
3. `OpenAiEmbeddingService` (dense; POST `/v1/embeddings` via `IHttpClientFactory`; batch 100; rate-limited) + `EmbeddingServiceFactory`
4. `QdrantVectorRepository` (gRPC via `Qdrant.Client`) + `QdrantCollectionManager` (`EnsureCollectionAsync` idempotent; payload indexes: `scope_type`, `scope_id`, `agent_id`, `is_stale`, `is_pinned`, `domain`, `product`, `module`, `content_type`, `security_level`, `source_id`, `document_id`)
5. DB migration `AddRagKnowledgePipeline` — 5 new entities (`KnowledgeSourceEntity`, `KnowledgeDocumentEntity`, `KnowledgeDocumentVersionEntity`, `KnowledgeChunkEntity`, `IngestionJobEntity`) + `AgentDefinitionEntity.KnowledgeProfileJson` + `KnowledgeSourceEntity.AgentId` + `RagConfigEntity`; DivaDbContext: 5 new DbSets + query filters + indexes
6. `KnowledgeSourceService` (CRUD + cache invalidation + `GetOrCreateAgentSourceAsync` with unique index `TenantId+AgentId+SourceType`) + `KnowledgeDocumentService` (3-tier hash-diff: ExternalVersion → ContentHash → ChunkHash)
7. `DocumentIngestionPipeline` (connect → chunk → enrich → 3-tier diff → embed → upsert; no entity links yet)
8. `IngestionWorkerService` (`IHostedService`; polls `IngestionJobEntity WHERE Status="Pending"` every 5 s; `SemaphoreSlim(MaxConcurrentIngestionJobs)`; `ConcurrentDictionary` for cancellation; SSE streaming via `IngestionProgressEvent`; mirrors `SchedulerHostedService` pattern)
9. `FileDocumentConnector` (walks allowed paths; reads via DI-injected `IPdfReader`, `IOfficeReader`, plain text; `LastWriteTimeUtc:Length` as ExternalVersion)
10. `MetadataFilterBuilder` (builds Qdrant `must`/`should` filter; 4 scope branches: tenant via `scope_id`, group via `scope_id`, platform, agent via `agent_id` — agent branch uses `agent_id` field exclusively)
11. `KnowledgeRetriever` (dense only: embed → filter → Qdrant search; no reranker; prepends `IsPinned` chunks) + `ContextAssembler` (`"Source: {title}\n{text}\n---"` per chunk)
12. `X-Agent-Id: {definition.Id}` injection in `AnthropicAgentRunner` outbound MCP calls + `McpServerContext.AgentId` property from `X-Agent-Id` header
13. `KnowledgeRetrievalMcpTools` — `search_knowledge`, `list_knowledge_sources` (includes agent-scoped sources), `get_document`, `index_file_to_knowledge` (injects `IDocumentIngestionPipeline`, `IFileSystemPathGuard`, `IPdfReader`, `IOfficeReader`; `AgentFileMaxSizeKb` guard; `GetOrCreateAgentSourceAsync` for auto-created source)
14. `SupervisorState.SupervisorDefinition` field + seeded supervisor `AgentDefinitionEntity` row (loaded by name at startup) + `RagContextStage` (guard: `AutoInjectContext || AutoQuery`; default false; sets `state.RetrievedContext` + `state.RetrievedChunks`)
15. `RagConfigEntity` + `GET|PUT /api/rag/settings` + `POST /api/rag/settings/test` (Qdrant health + embedding API key ping)
16. `RagController` (sources CRUD + `POST /{id}/ingest` SSE + `GET /{id}/jobs` + `GET /jobs/{jobId}` + `DELETE /jobs/{jobId}` cancel + `GET /{id}/estimate` + `POST /query`)
17. Docker compose: Qdrant `v1.9.0` (REST 6333 + gRPC 6334) + `qdrant_data` volume + `QDRANT__SERVICE__API_KEY`; `appsettings.json` `RAG` section (`Enabled: false`); `.env.example` additions
18. Admin portal: `RagSettings.tsx` (4 sections: Vector Store, Embedding, Retrieval, Ingestion Worker), `KnowledgeSources.tsx` (4 tabs: My Sources | Group Sources | Platform Sources | Agent Sources), `KnowledgeSourceEditor.tsx` (File + Agent scope; File + HTTP config panels; MetadataTaxonomyPanel; SchedulePanel), `KnowledgeExplorer.tsx` (query + scope toggles + filter pills + pinning controls), `IngestionJobs.tsx`
19. `AgentBuilder.tsx` Knowledge Profile tab: `autoInjectContext` (conditional on `ArchetypeId === "supervisor"`), `autoQuery`, `includeAgentSources`, `includeGroupSources`, `includePlatformSources`, domain/product/module/contentType filters, `maxResults`, agent sources sub-panel
20. Agent delete cleanup hook in `KnowledgeSourceService.DeleteAgentAsync` (background cascade: `IVectorRepository.DeleteBySourceAsync` per agent-scoped source → DB row delete)
21. MSW mock handlers for all `/api/rag/*` endpoints

**Verification:**
- `dotnet build Diva.slnx` — zero errors; `dotnet test` — all existing tests green
- `docker compose up -d` → Qdrant dashboard at `http://localhost:6333/dashboard`
- `POST /api/rag/settings/test` → `{ "qdrant": "ok", "embedding": "ok" }`
- `POST /api/rag/sources` (File, `docs/` folder) → 201; `POST /{id}/ingest` → SSE streams progress; job Completed
- `POST /api/rag/query { "query": "supervisor pipeline stages" }` → chunks from `docs/arch-supervisor.md`
- Agent calls `search_knowledge("supervisor pipeline")` during ReAct → cites retrieved sources
- Agent calls `index_file_to_knowledge("docs/arch-overview.md")` → `{ chunksIndexed: N }`; subsequent `search_knowledge` returns agent-scoped chunks
- File > 1 MB → `index_file_to_knowledge` returns size error
- Re-ingest unchanged source → `ChunksSkipped = N`, `ChunksAdded = 0` (hash-diff working)
- Supervisor with `autoInjectContext = true` → `state.RetrievedContext` non-null; `DecomposeStage` prompt contains `## Retrieved Knowledge`

---

### Phase 26.2 — Agent Memory
**Weeks 4–5 | Depends on 26.1**

**Deliverable:** Agents persist and semantically retrieve state across turns using the `diva_agent_memory` Qdrant collection. Working memories expire at session end. Episodic memories distilled via `summarize_and_archive`.

**Implementation steps:**
1. `AgentMemoryEntity` — `(Id, TenantId, AgentId, SessionId?, MemoryType, Content, VectorId, TagsJson, ExpiresAt?, CreatedAt)`; indexes `(TenantId, AgentId, MemoryType)`, `(TenantId, SessionId)`, `(ExpiresAt)`; EF migration `AddAgentMemory`
2. `QdrantCollectionManager.EnsureMemoryCollectionAsync` — creates `diva_agent_memory` collection (same dimensions as `diva_knowledge`); payload indexes: `tenant_id`, `agent_id`, `session_id`, `memory_type`, `expires_at`
3. `IAgentMemoryService` + `AgentMemoryService` in `Diva.Rag/Services/` — `SaveAsync`, `RecallAsync` (embed + Qdrant search), `ForgetAsync` (delete from Qdrant + SQLite), `SummarizeAndArchiveAsync` (LLM condense → upsert episodic → delete working)
4. `MemoryCleanupService` (`IHostedService`; polls `AgentMemoryEntity WHERE ExpiresAt < UtcNow` every `MemoryCleanupIntervalMinutes`; batch Qdrant delete + DB delete); hooked into `AgentSessionService.CloseSessionAsync` for session-end cleanup
5. Memory MCP tools added to `KnowledgeRetrievalMcpTools`: `save_memory`, `recall_memory`, `forget_memory`, `summarize_and_archive`
6. `KnowledgeProfileJson` memory fields: `enableMemory`, `memoryAutoRecall`, `memoryAutoRecallTypes`
7. `memoryAutoRecall` pre-fetch in `AnthropicAgentRunner`: if `definition.KnowledgeProfileJson.MemoryAutoRecall = true`, call `IAgentMemoryService.RecallAsync(request.Query)` before first LLM call → inject `## Agent Memory` block into dynamic system prompt
8. Memory API endpoints in `RagController`: `GET /api/rag/agents/{agentId}/memories`, `DELETE /api/rag/memories/{memoryId}`, `DELETE /api/rag/agents/{agentId}/memories`, `POST /api/rag/agents/{agentId}/memories/archive`
9. `AgentMemory.tsx` admin page (memory browser: type tabs, content preview, tags, expires at, forget/archive actions)
10. `AgentBuilder.tsx` memory toggles: `enableMemory`, `memoryAutoRecall`, `memoryAutoRecallTypes`
11. `RagOptions` memory fields wired to `appsettings.json`: `EnableAgentMemory`, `MemoryCollectionName`, `WorkingMemoryDefaultTtlMinutes`, `EpisodicMemoryDefaultTtlDays`, `MemoryCleanupIntervalMinutes`
12. MSW mock handlers for memory endpoints

**Verification:**
- `save_memory("Orders table has 2.4M rows", "working")` → `{ memoryId, expiresAt }`
- `recall_memory("database performance")` → returns that memory semantically
- `recall_memory(currentSessionOnly=true)` → only current session's working memories
- `summarize_and_archive` → one episodic memory created; working memories deleted from Qdrant + SQLite
- Session close → `MemoryCleanupService` hook fires; expired working memories gone
- Agent with `memoryAutoRecall=true` → `## Agent Memory` block in system prompt before first LLM call
- `AgentMemory.tsx` lists memories by type; "Forget" removes from both stores

---

### Phase 26.3 — Enterprise Connectors
**Weeks 6–9 | Depends on 26.1**

**Deliverable:** All 7 enterprise source types indexed. Webhook-triggered single-document re-index for Confluence, GitLab, and Jira. PII/secret scrubbing before embedding.

**Implementation steps:**
1. `IContentScrubber` interface + `PatternContentScrubber` (redacts: `sk-*`, `AKIA*`, `ghp_*`, email patterns, credit card patterns; runs in `DocumentIngestionPipeline` before embedding); wire into pipeline
2. `HttpDocumentConnector` (GET + `HtmlAgilityPack` HTML→text; ETag / response SHA-256 as ExternalVersion)
3. `ConfluenceDocumentConnector` (Atlassian REST API v2; space traversal; `page.version.number` as ExternalVersion) + `ConfluenceStorageFormatParser` (XML storage format → Markdown)
4. `JiraDocumentConnector` (Jira REST API v3; JQL pagination; issue + comments → text; `issue.fields.updated` as ExternalVersion; `jira:issue_updated` webhook support)
5. `GitLabDocumentConnector` (GitLab REST API v4; file tree + raw content; incremental diff via `commits?since=lastIndexedAt`; MR descriptions; `LastCommitSha` in ConfigJson; push/MR/pipeline webhook events)
6. `SqlServerSchemaConnector` (`INFORMATION_SCHEMA` + `sys.objects`; one doc per table/SP/view; FK→entity links; `OBJECT_MODIFY_DATE` as ExternalVersion; `WindowsIntegrated` + `SqlLogin` auth)
7. `DocumentStoreConnector` (SharePoint REST API + Windows UNC share; reuses `IOfficeReader`, `IPdfReader` via DI; ETag / `LastWriteTimeUtc:Length` as ExternalVersion)
8. `WebhooksController` (`POST /api/rag/webhooks/confluence`, `/gitlab`, `/jira`; HMAC-SHA256 validation per source; queues `IngestionJobEntity` with `DocumentUri` + `TriggerType="webhook"`; `page_deleted` → `IsActive=false` + Qdrant delete; stores raw event in `WebhookEventEntity`)
9. `WebhookEventEntity` DB entity + migration `AddWebhookEvents`; `GET /api/rag/webhooks/events` audit endpoint
10. `GET /api/rag/sources/{id}/estimate` (dry-run: token count + estimated embedding cost before triggering ingest)
11. Admin portal connector config panels added to `KnowledgeSourceEditor.tsx`: `ConfluenceConfigPanel`, `JiraConfigPanel`, `GitLabConfigPanel`, `SqlServerConfigPanel`, `SharePointConfigPanel` (all with webhook endpoint info box where applicable)
12. `WebhookEvents.tsx` audit log page; `DocumentVersions.tsx` version history page
13. MSW mock handlers for new connector types and webhook endpoints

**Verification:**
- Confluence space sync → `search_knowledge` returns Confluence page content
- Page updated in Confluence → webhook fires → only that page re-indexed → updated content retrievable within 30 s
- `page_deleted` webhook → `KnowledgeDocumentEntity.IsActive = false`; Qdrant points deleted
- Jira JQL sync → issues retrievable via `search_knowledge("authentication bug")`
- GitLab repo sync → code chunks retrievable; incremental re-index on push webhook touches only changed files
- SQL Server sync → table schema chunks in Qdrant; FK relationships appear as entity links
- File containing `sk-abc123` → `PatternContentScrubber` redacts before embedding; redacted text in chunk
- `GET /api/rag/sources/{id}/estimate` returns token count + cost estimate before ingest

---

### Phase 26.4 — Advanced Retrieval
**Weeks 10–12 | Depends on 26.1 and 26.3**

**Deliverable:** Hybrid BM25+dense search active. Cross-system entity links extracted during ingestion. Multi-hop retrieval follows links 1–2 hops. LLM reranker top-20 → top-5. Full Platform → Group → Tenant → Agent scope hierarchy.

**Implementation steps:**
1. Qdrant collection upgrade: add `sparse_vectors` config (FastEmbed BM25 server-side sparse vectorizer, Qdrant ≥ 1.8); `QdrantCollectionManager.EnsureCollectionAsync` updated; `EnableHybridSearch` flag gates hybrid path
2. `MetadataFilterBuilder` — Platform scope (no tenant/group filter) + Group scope via `IGroupMembershipCache` (resolve caller's group IDs at query time); `KnowledgeRetriever` passes `groupIds` from `IGroupMembershipCache`
3. `EntityLinkExtractor` (regex pipeline: Jira `[A-Z]+-\d+`, Confluence `/pages/\d+`, GitLab `!\d+`, SQL table names, C# class names, Git SHA `[0-9a-f]{7,40}`); runs in `DocumentIngestionPipeline` after chunking; populates `KnowledgeChunkEntity.EntityLinksJson` + Qdrant payload `entity_links[]`
4. `MultiHopRetriever` (wraps `KnowledgeRetriever`): primary search → extract entity IDs from top-5 → Qdrant payload filter for linked chunks → merge secondary chunks → deduplicate → pass combined set to reranker; `entityLinkHops` from `KnowledgeProfileJson` (0 = flat, 1 = one hop, 2 = two hops)
5. `LlmReranker` (top-20 candidates + query → LLM cross-score prompt → ranked JSON → top-5; fallback to score order on LLM failure or timeout)
6. `KnowledgeRetriever` updated to orchestrate: embed → hybrid Qdrant search (dense + sparse) → `MultiHopRetriever` → `LlmReranker` → prepend pinned chunks → `ContextAssembler`
7. Platform-level `AgentKnowledgeProfile` defaults registry (keyed by `ArchetypeId`; platform admin configures defaults for "rag", "code-analyst", "support", "ops", "requirements"); per-agent `KnowledgeProfileJson` overrides
8. `memoryAutoRecall` fully wired: `AnthropicAgentRunner` checks `MemoryAutoRecall` flag; cross-session recall from episodic/semantic tiers
9. `DocumentVersions.tsx` admin page + `KnowledgeExplorer.tsx` entity-link chips (clickable → fetch linked chunk)
10. `KnowledgeExplorer.tsx` Group/Platform scope toggles fully active (Phase 26.1 had tenant + agent only)
11. `AgentBuilder.tsx` Knowledge Profile tab: `entityLinkHops` slider (0|1|2), Group/Platform source toggles fully functional

**Verification:**
- `search_knowledge` with `EnableHybridSearch=true` → results from both dense and BM25 sparse paths
- Jira ticket referencing Confluence page `CONF-98765` → `recall_memory("auth design")` returns both the Jira chunk and the linked Confluence chunk in one query (entity link hop)
- Group-scoped source accessible to tenant members of that group; not accessible to other tenants
- Platform-scoped source accessible to all tenants
- Agent with `ArchetypeId="code-analyst"` → platform default profile auto-applies `contentTypes: [code-file, adr]` without explicit filter
- LLM reranker re-orders top-20 candidates; result set quality visibly improved for domain queries

---

### Phase 26.5 — Dev Workflow Agents
**Weeks 13–18 | Depends on 26.3 and 26.4**

**Deliverable:** AI-driven SDLC pipeline: Jira story created → requirements analysis → architecture proposal → human approval gate → C# scaffolding + EF migration → unit tests → GitLab MR created → PR review → Confluence docs updated.

**Implementation steps:**
1. `GitLabApiService` (write API: `CreateMrAsync`, `AddMrCommentAsync`, `GetMrDiffAsync`, `ApproveMrAsync`, `UpdateMrAsync`; separate from read-only `GitLabDocumentConnector`)
2. `GitLabMcpTools` (`[McpServerToolType]`; tools: `create_mr`, `add_mr_comment`, `get_mr_diff`, `approve_mr`); registered via `.WithDivaMcpTools<GitLabMcpTools>()` in `Program.cs`
3. Agent definitions (system prompts, `KnowledgeProfileJson`, `DelegateAgentIdsJson`, tool bindings) for:
   - `RequirementsAnalystAgent` (Jira + Confluence knowledge; outputs structured tech spec as Jira comment)
   - `ArchitectureAgent` (ADRs + existing code patterns; outputs architecture proposal → human gate)
   - `BackendCodeAgent` (.NET + SQL Server knowledge; generates C# + EF migrations via file tools)
   - `FrontendCodeAgent` (Angular/TypeScript knowledge; generates components)
   - `TestAgent` (code under test + test patterns; generates NUnit/Karma specs)
   - `PRReviewAgent` (coding standards + ADRs + MR diff; posts GitLab MR comments)
   - `DocumentationAgent` (Confluence write API; updates pages from code diff)
   - `PipelineAgent` (GitLab CI/CD failure logs + known patterns; root cause + fix suggestion)
   - `DBMigrationAgent` (SQL schema + EF migration history; migration safety review)
4. SDLC Coordinator wired via Phase 19 `DelegateAgentIdsJson`: Requirements → Architecture → Code → Test → Documentation pipeline
5. Human governance gates: Architecture proposal → Jira comment + `"Awaiting Approval"` status transition; `RequirementsAnalystAgent` notified on Jira status change via webhook; MR gate enforced by GitLab branch protection
6. Webhook triggers: Jira `issue_created` (type=Story) → `POST /api/agents/invoke` to start `RequirementsAnalystAgent`; GitLab push to feature branch → trigger `PRReviewAgent`
7. Agent catalog UI page in admin portal: pre-built workflow template cards (SDLC pipeline, IT support, incident response)

**Verification:**
- Create Jira story "Add OAuth2 PKCE flow" → `RequirementsAnalystAgent` posts tech spec as Jira comment within 60 s
- Architect approves in Jira → `BackendCodeAgent` scaffolds C# + EF migration + NUnit tests
- `PRReviewAgent` reviews GitLab MR → posts code review comments referencing ADRs from Confluence
- `PipelineAgent` on CI failure → diagnoses root cause from pipeline logs + knowledge base

---

### Phase 26.6 — Business Workflow Agents
**Weeks 19–22 | Depends on 26.3 and 26.4**

**Deliverable:** Automated IT support, incident response, compliance gap analysis, and project status reporting via scheduled and webhook-triggered agents.

**Implementation steps:**
1. Agent definitions for:
   - `ITSupportAgent` (Confluence runbooks + Jira past tickets; auto-classifies + routes new support tickets; posts resolution steps)
   - `IncidentResponseAgent` (deployment history + SQL schema + logs; root cause analysis; posts to Jira incident ticket)
   - `ProjectStatusAgent` (Jira sprint data + GitLab pipeline status; generates weekly stakeholder report as Confluence page)
   - `ComplianceAgent` (ADRs + security policies + code patterns; identifies compliance gaps; posts findings)
2. Scheduled triggers via existing `ScheduledTaskEntity` + `SchedulerHostedService`: `ProjectStatusAgent` weekly, `ComplianceAgent` monthly
3. Jira webhook `issue_created` (type=Support) → `ITSupportAgent` auto-triage within 60 s
4. Admin portal: agent catalog workflow template cards for business workflows

**Verification:**
- New Jira support ticket → `ITSupportAgent` posts classification + resolution steps within 60 s
- Weekly cron fires → `ProjectStatusAgent` creates Confluence status page with sprint metrics
- `ComplianceAgent` run → Confluence report listing policy gaps with ADR references

---

### Phase 26.7 — Quality & Scale
**Weeks 23–25 | Depends on 26.1 through 26.4**

**Deliverable:** Production-ready quality guarantees: retrieval eval metrics, cost circuit breakers, local embedding fallback, Qdrant maintenance, memory quality scoring.

**Implementation steps:**
1. `RagEvaluationRunEntity` (admin-configured test set: query → expected document IDs; stores hit-rate@5, MRR per run) + `POST /api/rag/evaluate` + `GET /api/rag/evaluate/runs` + `RagEvaluationResults.tsx` quality trend chart
2. Embedding API token-bucket rate limiter in `OpenAiEmbeddingService`; `MaxEmbeddingBatchesPerJob` circuit breaker (halt job when limit reached; log warning)
3. `AgentIndexingQuotaChunksPerDay` — per-agent daily limit on `index_file_to_knowledge` chunks (tracked in `AgentMemoryEntity` or dedicated counter; returns quota error when exceeded)
4. `OllamaEmbeddingService` (POST to local Ollama endpoint; for air-gapped / cost-sensitive deployments); `EmbeddingServiceFactory` selects provider from `RagOptions.EmbeddingProvider`
5. Qdrant collection maintenance `IHostedService`: periodic `OptimizeAsync` + stale point cleanup (delete points where `is_stale=true AND indexed_at < cutoff`); configurable retention window
6. Memory quality scoring: `AgentMemoryEntity.RelevanceScore (float?)` updated by background scorer (LLM-as-Judge on random sample); low-score working memories auto-evicted; admin sees quality distribution in `AgentMemory.tsx`
7. Cross-agent memory sharing: group-scoped episodic memories (`scope_type="group_memory"`, `scope_id=groupId`); platform-scoped semantic memories; `MetadataFilterBuilder` extended for memory collection
8. Feedback loop: `KnowledgeChunkEntity.ReviewFlag (bool)` + `ReviewNote (string?)`; admin flags incorrect/outdated chunks from `KnowledgeExplorer`; flagged chunks surfaced in dashboard for re-index or removal
9. `RagEvaluationResults.tsx` quality dashboard: hit-rate@5 trend chart, MRR trend, per-source quality breakdown

**Verification:**
- 30-day eval run history shows hit-rate@5 ≥ 0.8 for core domain queries
- Large GitLab repo (5,000 files) → `MaxEmbeddingBatchesPerJob` triggers at configured limit; job pauses gracefully
- `OllamaEmbeddingService` produces embeddings matching expected dimension; `search_knowledge` returns results
- Qdrant maintenance job runs → stale points older than retention window deleted; collection size stable
- Agent with daily quota exceeded → `index_file_to_knowledge` returns quota error with reset time

---

## Gap Analysis (Resolved in Phase 26.1)

Gaps identified during architectural review of Path 2 injection, agent-scoped index, and `index_file_to_knowledge`:

| # | Gap | Resolution |
|---|-----|-----------|
| **1** | `SupervisorAgent` has no `AgentDefinitionEntity` — `RagContextStage` can't read `autoInjectContext` | Add reserved supervisor `AgentDefinitionEntity` row (loaded by name at startup); add `SupervisorDefinition` to `SupervisorState`; `SupervisorAgent.InvokeAsync` populates it; `RagContextStage` reads `AutoInjectContext \|\| AutoQuery` (both flags trigger injection). Default = false if no definition |
| **2** | `KnowledgeProfileJson` not yet on `AgentDefinitionEntity` | Add field + include in `AddRagKnowledgePipeline` EF migration |
| **3** | `index_file_to_knowledge` can time out on large files (30s MCP tool timeout) | `MaxFileSizeKb` guard (default 1,024 KB) — reject oversized files with descriptive error; for large sources use admin ingestion pipeline instead |
| **4** | Deleting an agent leaves orphaned agent-scoped Qdrant points and `KnowledgeSourceEntity` rows | Agent delete cleanup hook in `KnowledgeSourceService` — cascade `IVectorRepository.DeleteBySourceAsync` + DB row delete; runs as background job |
| **5** | Auto-created agent-scoped source has no unique constraint — concurrent calls create duplicates | Unique index `(TenantId, AgentId, SourceType)` on `KnowledgeSourceEntity`; `GetOrCreateAgentSourceAsync` uses upsert semantics |
| **6** | `autoQuery = true` on supervisor definition silently ignored (`AnthropicAgentRunner` not invoked by supervisor) | `RagContextStage` treats `AutoInjectContext \|\| AutoQuery` as the trigger; both flags are equivalent for supervisor flow |
| **7** | `list_knowledge_sources` tool may omit agent-scoped sources | Implementation explicitly includes sources where `ScopeType="agent" AND AgentId=ctx.AgentId` alongside tenant/group/platform sources |
| **8** | No per-agent cost quota for `index_file_to_knowledge` embedding calls | `MaxFileSizeKb` guard limits per-call embedding cost; `MaxEmbeddingBatchesPerJob` from `RagOptions` applies as circuit breaker |
| **9** | `autoInjectContext` shown for all agents in AgentBuilder — only meaningful for supervisors | `autoInjectContext` toggle shown conditionally when `agent.ArchetypeId === "supervisor"`; tooltip explains scope |
| **10** | Agent-scoped Qdrant filter uses `agent_id` not `scope_id` — asymmetry in `MetadataFilterBuilder` | 4th branch in `should` filter uses `agent_id` key exclusively; explicitly documented; other branches unchanged |
| **11** | Phase 25.1 implementation order was incomplete | Updated to 20 steps (see Implementation Order above) |

---

## Known Gaps & Deferred Items

| Item | Deferred to |
|------|-------------|
| Hybrid search (BM25 sparse via Qdrant FastEmbed) | Phase 26.4 |
| Entity link extraction + multi-hop retrieval | Phase 26.4 |
| LLM reranker (26.1 uses score order) | Phase 26.4 |
| Platform/Group scope hierarchy (26.1 is tenant + agent only) | Phase 26.4 |
| ArchetypeId platform-level default profiles | Phase 26.4 |
| `memoryAutoRecall` cross-session pre-fetch | Phase 26.4 |
| PII/secret content scrubber | Phase 26.3 |
| Confluence/Jira/GitLab/SQL Server/SharePoint/HTTP connectors | Phase 26.3 |
| Webhooks (Confluence, GitLab, Jira) | Phase 26.3 |
| Dev workflow agent configurations + GitLab write API | Phase 26.5 |
| Business workflow agent configurations | Phase 26.6 |
| Retrieval quality evaluation (`RagEvaluationRunEntity`) | Phase 26.7 |
| Per-agent daily indexing quota (`AgentIndexingQuotaChunksPerDay`) | Phase 26.7 |
| `OllamaEmbeddingService` | Phase 26.7 |
| Qdrant collection maintenance job (stale point cleanup) | Phase 26.7 |
| Cross-agent memory sharing (group/platform episodic memories) | Phase 26.7 |
| Memory quality scoring + auto-eviction | Phase 26.7 |

---

> **Verification checklists are embedded in each sub-phase section above.**
