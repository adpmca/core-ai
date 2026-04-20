namespace Diva.Agents.Archetypes;

using Diva.Core.Configuration;

/// <summary>
/// Registry of built-in agent archetypes. Tenant admins select an archetype
/// when creating an agent — it pre-fills defaults and attaches standard hooks.
/// </summary>
public static class BuiltInArchetypes
{
    public static readonly AgentArchetype General = new()
    {
        Id = "general",
        DisplayName = "General Assistant",
        Description = "Versatile agent for open-ended tasks. No specialised pre-processing.",
        Icon = "bot",
        Category = "General",
        SystemPromptTemplate = "You are a helpful AI assistant for {{company_name}}. Answer user questions accurately and concisely.",
        DefaultCapabilities = ["general", "question-answering", "summarization"],
        DefaultHooks = new()
        {
            ["OnInit"] = "PromptInjectionGuardHook",
        },
        DefaultTemperature = 0.7,
        DefaultMaxIterations = 10,
    };

    public static readonly AgentArchetype Rag = new()
    {
        Id = "rag",
        DisplayName = "RAG Knowledge Agent",
        Description = "Retrieval-Augmented Generation agent. Always grounds answers in retrieved documents. Ideal for knowledge bases, documentation QA, and policy lookup.",
        Icon = "database",
        Category = "Knowledge",
        SystemPromptTemplate = """
            You are a knowledge assistant for {{company_name}}.
            ALWAYS search the knowledge base before answering.
            ALWAYS cite your sources with document names and section references.
            If the knowledge base does not contain relevant information, say so explicitly.
            Never fabricate information not found in retrieved documents.
            """,
        DefaultCapabilities = ["rag", "knowledge-base", "document-search", "citation"],
        SuggestedTools = ["knowledge-search", "document-retrieval"],
        DefaultHooks = new()
        {
            ["OnInit"] = "PromptInjectionGuardHook",
            ["OnBeforeResponse"] = "CitationEnforcerHook",
            ["OnAfterResponse"] = "AuditTrailHook",
        },
        DefaultTemperature = 0.3,
        DefaultMaxIterations = 8,
        DefaultVerificationMode = "ToolGrounded",
    };

    public static readonly AgentArchetype CodeAnalyst = new()
    {
        Id = "code-analyst",
        DisplayName = "Code Analyst",
        Description = "Specialised in code review, debugging, refactoring suggestions, and technical documentation. Enforces structured output for code blocks.",
        Icon = "code",
        Category = "Engineering",
        SystemPromptTemplate = """
            You are a senior software engineer and code analyst for {{company_name}}.
            When reviewing code:
            1. Identify bugs, security issues, and performance problems
            2. Suggest concrete fixes with code examples
            3. Follow the team's coding conventions
            4. Use markdown code blocks with language identifiers
            Language: {{primary_language}}
            """,
        DefaultCapabilities = ["code-review", "debugging", "refactoring", "documentation"],
        SuggestedTools = ["code-search", "linter", "test-runner"],
        DefaultTemperature = 0.2,
        DefaultMaxIterations = 15,
        DefaultVerificationMode = "LlmVerifier",
    };

    public static readonly AgentArchetype DataAnalyst = new()
    {
        Id = "data-analyst",
        DisplayName = "Data Analyst",
        Description = "Analyses datasets, generates SQL queries, creates chart descriptions, and explains statistical findings.",
        Icon = "chart",
        Category = "Analytics",
        SystemPromptTemplate = """
            You are a data analyst for {{company_name}}.
            When analysing data:
            1. Always validate your SQL/queries before presenting results
            2. Present findings with clear metrics and comparisons
            3. Use markdown tables for structured data
            4. Explain statistical significance when relevant
            Database: {{database_type}}
            """,
        DefaultCapabilities = ["data-analysis", "sql", "statistics", "visualization", "reporting"],
        SuggestedTools = ["sql-query", "data-export", "chart-generator"],
        DefaultHooks = new()
        {
            ["OnInit"] = "PromptInjectionGuardHook",
            ["OnBeforeResponse"] = "PiiRedactionHook",
            ["OnAfterResponse"] = "AuditTrailHook",
        },
        DefaultTemperature = 0.3,
        DefaultMaxIterations = 12,
        DefaultVerificationMode = "Strict",
    };

    public static readonly AgentArchetype Researcher = new()
    {
        Id = "researcher",
        DisplayName = "Research Agent",
        Description = "Deep-dive research agent that systematically explores topics using multiple sources and produces structured reports with citations.",
        Icon = "search",
        Category = "Research",
        SystemPromptTemplate = """
            You are a research specialist for {{company_name}}.
            Your research methodology:
            1. Break the question into sub-questions
            2. Search multiple sources for each sub-question
            3. Cross-reference findings across sources
            4. Synthesise into a structured report with sections
            5. Always cite sources and note confidence levels
            Focus area: {{research_domain}}
            """,
        DefaultCapabilities = ["research", "web-search", "synthesis", "report-generation", "citation"],
        SuggestedTools = ["web-search", "document-retrieval", "knowledge-search"],
        DefaultTemperature = 0.5,
        DefaultMaxIterations = 20,
        DefaultVerificationMode = "LlmVerifier",
        PipelineStageDefaults = new()
        {
            ["Decompose"] = true,
        },
    };

    public static readonly AgentArchetype Coordinator = new()
    {
        Id = "coordinator",
        DisplayName = "Multi-Agent Coordinator",
        Description = "Orchestrates sub-tasks across multiple agents. Decomposes complex requests, delegates to specialised agents, and integrates results.",
        Icon = "network",
        Category = "Orchestration",
        SystemPromptTemplate = """
            You are a task coordinator for {{company_name}}.
            Your role is to break complex requests into sub-tasks and delegate them to specialised agents.
            Available agents will be provided as tools.
            For each sub-task:
            1. Identify the best agent for the job
            2. Formulate a clear, self-contained instruction
            3. Collect and synthesise results
            4. Resolve any conflicts between agent outputs
            """,
        DefaultCapabilities = ["orchestration", "delegation", "synthesis", "planning"],
        DefaultTemperature = 0.4,
        DefaultMaxIterations = 25,
        PipelineStageDefaults = new()
        {
            ["Decompose"] = true,
            ["CapabilityMatch"] = true,
        },
    };

    public static readonly AgentArchetype Conversational = new()
    {
        Id = "conversational",
        DisplayName = "Conversational Agent",
        Description = "Optimised for multi-turn conversation with memory, personality, and emotional intelligence. Ideal for customer support and guided workflows.",
        Icon = "message-circle",
        Category = "Communication",
        SystemPromptTemplate = """
            You are a conversational AI assistant for {{company_name}}.
            Personality: {{personality_traits}}
            Guidelines:
            1. Be warm, empathetic, and professional
            2. Remember context from earlier in the conversation
            3. Ask clarifying questions when needed
            4. Guide users through multi-step processes
            5. Escalate to human support when you cannot help
            """,
        DefaultCapabilities = ["conversation", "customer-support", "onboarding", "faq"],
        DefaultHooks = new()
        {
            ["OnInit"] = "PromptInjectionGuardHook",
            ["OnBeforeResponse"] = "DisclaimerAppenderHook",
            ["OnAfterResponse"] = "AuditTrailHook",
        },
        DefaultTemperature = 0.8,
        DefaultMaxIterations = 6,
        DefaultVerificationMode = "Off",
        DefaultExecutionMode = AgentExecutionMode.ChatOnly,
    };

    public static readonly AgentArchetype RemoteA2A = new()
    {
        Id = "remote-a2a",
        DisplayName = "Remote A2A Agent",
        Description = "Proxy agent that delegates to an external agent via the A2A protocol. Configure the remote endpoint URL and authentication.",
        Icon = "globe",
        Category = "Federation",
        SystemPromptTemplate = "",
        DefaultCapabilities = ["a2a", "remote", "federation"],
        DefaultTemperature = 0,
        DefaultMaxIterations = 1,
    };

    /// <summary>All built-in archetypes indexed by ID.</summary>
    public static readonly IReadOnlyDictionary<string, AgentArchetype> All =
        new Dictionary<string, AgentArchetype>(StringComparer.OrdinalIgnoreCase)
        {
            [General.Id] = General,
            [Rag.Id] = Rag,
            [CodeAnalyst.Id] = CodeAnalyst,
            [DataAnalyst.Id] = DataAnalyst,
            [Researcher.Id] = Researcher,
            [Coordinator.Id] = Coordinator,
            [Conversational.Id] = Conversational,
            [RemoteA2A.Id] = RemoteA2A,
        };
}
