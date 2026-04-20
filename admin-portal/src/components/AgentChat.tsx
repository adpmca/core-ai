import { useState, useRef, useEffect } from "react";
import { useNavigate, useParams, useLocation } from "react-router";
import DOMPurify from "dompurify";
import { toast } from "sonner";
import {
  ArrowLeft,
  Bot,
  ChevronDown,
  ChevronRight,
  RotateCcw,
  Send,
  User,
  Wrench,
} from "lucide-react";
import {
  api,
  type AgentSummary,
  type AgentStreamChunk,
  type LlmConfig,
  type AvailableLlmConfig,
  type VerificationResult,
  type FollowUpQuestion,
} from "@/api";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import { Switch } from "@/components/ui/switch";
import { Label } from "@/components/ui/label";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Separator } from "@/components/ui/separator";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import { cn } from "@/lib/utils";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

interface ToolCall {
  name: string;
  input: string;
  output?: string;
}

interface Iteration {
  number: number;
  thinking?: string;
  toolCalls: ToolCall[];
}

interface TimelineEntry {
  time: number;
  type: string;
  iteration?: number;
  detail: string;
}

interface Message {
  role: "user" | "agent";
  text: string;
  iterations?: Iteration[];
  toolsUsed?: string[];
  executionTime?: string;
  verification?: VerificationResult;
  followUpQuestions?: FollowUpQuestion[];
  error?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Verification Badge
// ─────────────────────────────────────────────────────────────────────────────

function VerificationBadge({ v }: { v: VerificationResult }) {
  if (v.mode === "Off") return null;
  if (v.wasBlocked) return (
    <Badge variant="destructive" className="text-xs">Blocked — low confidence</Badge>
  );
  if (v.isVerified) return (
    <Badge className="text-xs bg-emerald-600 hover:bg-emerald-600">
      Verified {Math.round(v.confidence * 100)}%
    </Badge>
  );
  return (
    <Badge variant="outline" className="text-xs border-amber-500 text-amber-500">
      Unverified{v.ungroundedClaims.length > 0 ? ` · ${v.ungroundedClaims.length} unsupported claim(s)` : ""}
    </Badge>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Iteration Trace
// ─────────────────────────────────────────────────────────────────────────────

function IterationTrace({ iterations, detailed }: { iterations: Iteration[]; detailed: boolean }) {
  const [open, setOpen] = useState(false);

  return (
    <Collapsible open={open} onOpenChange={setOpen} className="mt-2">
      <CollapsibleTrigger className="flex items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors">
        {open ? <ChevronDown className="size-3" /> : <ChevronRight className="size-3" />}
        <Wrench className="size-3" />
        {iterations.length} iteration{iterations.length !== 1 ? "s" : ""}
      </CollapsibleTrigger>
      <CollapsibleContent>
        <div className="mt-2 rounded-md border bg-muted/30 p-3 space-y-4 text-xs max-h-96 overflow-y-auto">
          {iterations.map((iter) => (
            <div key={iter.number} className="space-y-2">
              <div className="font-semibold text-indigo-400">Iteration {iter.number}</div>
              {iter.thinking && (
                <p className="italic text-muted-foreground pl-2 border-l-2 border-muted">
                  {detailed ? iter.thinking : (iter.thinking.length > 200 ? iter.thinking.slice(0, 200) + "…" : iter.thinking)}
                </p>
              )}
              {iter.toolCalls.map((tc, ti) => (
                <div key={ti} className="pl-2 border-l-2 border-amber-600/50 space-y-1">
                  <div className="font-medium text-amber-400 flex items-center gap-1">
                    <Wrench className="size-3" />{tc.name}
                    {tc.output === undefined && <span className="text-muted-foreground ml-1">⏳</span>}
                  </div>
                  <pre className="text-[11px] text-foreground/70 whitespace-pre-wrap break-words bg-background/50 rounded p-1.5 overflow-auto max-h-24">
                    {tc.input}
                  </pre>
                  {tc.output !== undefined && (
                    <pre className="text-[11px] text-emerald-400 whitespace-pre-wrap break-words bg-background/50 rounded p-1.5 overflow-auto max-h-28">
                      {tc.output}
                    </pre>
                  )}
                </div>
              ))}
            </div>
          ))}
        </div>
      </CollapsibleContent>
    </Collapsible>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Live streaming indicator
// ─────────────────────────────────────────────────────────────────────────────

function LiveFeed({
  iterations,
  status,
  plan,
  timeline,
  detailed,
}: {
  iterations: Iteration[];
  status: string;
  plan: { steps: string[]; revised: boolean } | null;
  timeline: TimelineEntry[];
  detailed: boolean;
}) {
  return (
    <div className="flex flex-col items-start gap-3">
      <div className="flex gap-2.5">
        <Avatar className="size-7 shrink-0 mt-0.5">
          <AvatarFallback className="bg-primary/10">
            <Bot className="size-3.5 text-primary" />
          </AvatarFallback>
        </Avatar>
        <div className="rounded-2xl rounded-tl-sm bg-muted px-4 py-3 max-w-[85%] min-w-64 space-y-2">
          <p className="text-sm text-muted-foreground">{status || "Thinking..."}</p>

          {plan && plan.steps.length > 0 && (
            <div className="rounded border border-indigo-500/30 bg-indigo-500/5 p-2.5 space-y-1">
              <p className="text-[11px] font-semibold text-indigo-400">
                {plan.revised ? "Revised Plan" : "Plan"}
              </p>
              {plan.steps.map((step, i) => (
                <p key={i} className="text-[11px] text-muted-foreground pl-2">{step}</p>
              ))}
            </div>
          )}

          {iterations.map((iter) => (
            <div key={iter.number} className="space-y-1.5">
              <p className="text-[11px] font-semibold text-indigo-400">Iteration {iter.number}</p>
              {iter.thinking && (
                <p className="text-[11px] italic text-muted-foreground pl-2 border-l-2 border-muted-foreground/30">
                  {detailed ? iter.thinking : iter.thinking.slice(0, 200) + (iter.thinking.length > 200 ? "…" : "")}
                </p>
              )}
              {iter.toolCalls.map((tc, ti) => (
                <div key={ti} className="pl-2 border-l-2 border-amber-600/50 space-y-1">
                  <p className="text-[11px] font-medium text-amber-400 flex items-center gap-1">
                    <Wrench className="size-2.5" />{tc.name} {tc.output === undefined ? "⏳" : "✓"}
                  </p>
                  <pre className="text-[10px] text-foreground/60 whitespace-pre-wrap break-words bg-background/30 rounded px-1.5 py-1 overflow-auto max-h-16">
                    {tc.input}
                  </pre>
                  {tc.output !== undefined && (
                    <pre className="text-[10px] text-emerald-400 whitespace-pre-wrap break-words bg-background/30 rounded px-1.5 py-1 overflow-auto max-h-20">
                      {tc.output}
                    </pre>
                  )}
                </div>
              ))}
            </div>
          ))}
        </div>
      </div>

      {detailed && timeline.length > 0 && (
        <div className="ml-9 rounded-md border bg-muted/20 p-2 max-h-56 overflow-y-auto w-full max-w-[85%]">
          <p className="text-[11px] font-semibold text-indigo-400 mb-2">Event Log ({timeline.length})</p>
          <div className="mb-2 rounded border border-sky-500/20 bg-sky-500/5 px-2 py-1.5 text-[10px] text-muted-foreground">
            <span className="font-semibold text-sky-400">Hook Detail Legend:</span>{" "}
            <span className="font-mono">triggered</span>=rules fired, {" "}
            <span className="font-mono">filtered</span>=tool calls suppressed, {" "}
            <span className="font-mono">blocked</span>=policy blocked, {" "}
            <span className="font-mono">errorAction</span>=continue/retry/abort
          </div>
          {timeline.map((e, i) => {
            const colors: Record<string, string> = {
              tools_available: "text-emerald-400", plan: "text-indigo-400",
              plan_revised: "text-violet-400", iteration_start: "text-indigo-400",
              thinking: "text-muted-foreground", tool_call: "text-amber-400",
              tool_result: "text-emerald-400", final_response: "text-cyan-400",
              hook_executed: "text-sky-400",
              verification: "text-emerald-400", continuation_start: "text-fuchsia-400",
              correction: "text-red-400", error: "text-destructive", done: "text-emerald-400",
            };
            return (
              <div key={i} className="flex gap-1.5 text-[10px] font-mono mb-0.5">
                <span className="text-muted-foreground/60 shrink-0 w-10 text-right">
                  {(e.time / 1000).toFixed(1)}s
                </span>
                <span className={cn("shrink-0 w-28", colors[e.type] ?? "text-muted-foreground")}>
                  {e.type}
                </span>
                {e.iteration !== undefined && (
                  <span className="text-muted-foreground/60">#{e.iteration}</span>
                )}
                <span className="text-foreground/60 truncate">{e.detail}</span>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Main AgentChat component
// ─────────────────────────────────────────────────────────────────────────────

export function AgentChat() {
  const { id: agentId } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const agentFromState = (location.state as { agent?: AgentSummary } | null)?.agent;

  const [agent, setAgent] = useState<AgentSummary | undefined>(agentFromState);
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState("");
  const [loading, setLoading] = useState(false);
  const [sessionId, setSessionId] = useState<string | undefined>(undefined);
  const [llmConfig, setLlmConfig] = useState<LlmConfig>({ availableModels: [], currentProvider: "", defaultModel: "" });
  const [selectedModel, setSelectedModel] = useState<string>("");
  const [availableLlmConfigs, setAvailableLlmConfigs] = useState<AvailableLlmConfig[]>([]);
  const [selectedConfigId, setSelectedConfigId] = useState<number | undefined>(undefined);
  const [detailedMode, setDetailedMode] = useState(false);

  const [liveIterations, setLiveIterations] = useState<Iteration[]>([]);
  const [liveStatus, setLiveStatus] = useState<string>("");
  const [livePlan, setLivePlan] = useState<{ steps: string[]; revised: boolean } | null>(null);
  const [liveTimeline, setLiveTimeline] = useState<TimelineEntry[]>([]);

  const bottomRef = useRef<HTMLDivElement>(null);
  const abortRef = useRef<AbortController | null>(null);

  // Load agent definition (needed for llmConfigId) and available configs for the tenant
  useEffect(() => {
    if (!agentFromState && agentId) {
      api.getAgent(agentId)
        .then((a) => {
          const agentSummary = { id: a.id!, name: a.name, displayName: a.displayName ?? a.name, isEnabled: a.isEnabled, status: a.status ?? "Draft", agentType: a.agentType ?? "general", createdAt: "", llmConfigId: a.llmConfigId };
          setAgent(agentSummary);
          // Initialize config selector to agent's pinned config
          setSelectedConfigId(a.llmConfigId ?? undefined);
        })
        .catch((e: Error) => toast.error("Failed to load agent", { description: e.message }));
    } else if (agentFromState) {
      setSelectedConfigId(agentFromState.llmConfigId ?? undefined);
    }
  }, [agentId, agentFromState]);

  // Load all available LLM configs for the tenant (for the config picker)
  useEffect(() => {
    api.listAvailableLlmConfigs().then(setAvailableLlmConfigs).catch(() => {});
  }, []);

  // Re-resolve the LLM config whenever the selected config changes.
  // Uses the resolver endpoint so the model list matches the correct provider.
  useEffect(() => {
    api.getLlmConfig(selectedConfigId).then(setLlmConfig).catch(() => {});
    setSelectedModel(""); // reset model when config changes
  }, [selectedConfigId]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, liveIterations, liveStatus]);

  const clearChat = () => {
    abortRef.current?.abort();
    setMessages([]);
    setSessionId(undefined);
    setLiveIterations([]);
    setLiveTimeline([]);
    setLiveStatus("");
    setLivePlan(null);
  };

  const send = async () => {
    const query = input.trim();
    if (!query || loading || !agent) return;
    setInput("");
    setMessages((m) => [...m, { role: "user", text: query }]);
    setLoading(true);
    setLiveIterations([]);
    setLiveTimeline([]);
    setLiveStatus("Connecting...");

    const abort = new AbortController();
    abortRef.current = abort;

    const itersRef: Iteration[] = [];
    const timelineRef: TimelineEntry[] = [];
    const streamStart = Date.now();
    let pendingMsg: Message | null = null;

    const logEvent = (type: string, detail: string, iteration?: number) => {
      timelineRef.push({ time: Date.now() - streamStart, type, iteration, detail });
      setLiveTimeline([...timelineRef]);
    };

    const handleChunk = (chunk: AgentStreamChunk) => {
      switch (chunk.type) {
        case "tools_available":
          logEvent("tools_available", chunk.toolCount ? `${chunk.toolCount} tools: ${(chunk.toolNames ?? []).join(", ")}` : "No tools");
          setLiveStatus(chunk.toolCount ? `${chunk.toolCount} tool${chunk.toolCount > 1 ? "s" : ""} connected` : "No tools connected");
          break;
        case "plan":
          logEvent("plan", (chunk.planSteps ?? []).join(" → "));
          setLivePlan({ steps: chunk.planSteps ?? [], revised: false });
          setLiveStatus("Planning...");
          break;
        case "plan_revised":
          logEvent("plan_revised", (chunk.planSteps ?? []).join(" → "));
          setLivePlan({ steps: chunk.planSteps ?? [], revised: true });
          setLiveStatus("Replanning...");
          break;
        case "iteration_start":
          logEvent("iteration_start", `Iteration ${chunk.iteration}`, chunk.iteration);
          itersRef.push({ number: chunk.iteration!, toolCalls: [] });
          setLiveIterations([...itersRef]);
          setLiveStatus(`Iteration ${chunk.iteration}...`);
          break;
        case "text_delta": {
          // Accumulate streaming tokens into the current iteration's thinking text
          const iter = itersRef.find((i) => i.number === chunk.iteration);
          if (iter) {
            iter.thinking = (iter.thinking ?? "") + (chunk.content ?? "");
            setLiveIterations([...itersRef]);
          }
          break;
        }
        case "thinking": {
          logEvent("thinking", chunk.content ?? "", chunk.iteration);
          const iter = itersRef.find((i) => i.number === chunk.iteration);
          // Replace with full text from server (confirms accumulated text_delta content)
          if (iter) { iter.thinking = chunk.content; setLiveIterations([...itersRef]); }
          break;
        }
        case "tool_call": {
          logEvent("tool_call", `${chunk.toolName}(${(chunk.toolInput ?? "").slice(0, 120)})`, chunk.iteration);
          const iter = itersRef.find((i) => i.number === chunk.iteration);
          if (iter) {
            iter.toolCalls.push({ name: chunk.toolName!, input: chunk.toolInput ?? "" });
            setLiveIterations([...itersRef]);
            setLiveStatus(`Calling ${chunk.toolName}...`);
          }
          break;
        }
        case "tool_result": {
          logEvent("tool_result", `${chunk.toolName} → ${(chunk.toolOutput ?? "").slice(0, 120)}`, chunk.iteration);
          const iter = itersRef.find((i) => i.number === chunk.iteration);
          if (iter) {
            const call = [...iter.toolCalls].reverse().find((c) => c.name === chunk.toolName && c.output === undefined);
            if (call) { call.output = chunk.toolOutput; setLiveIterations([...itersRef]); }
          }
          setLiveStatus(`Iteration ${chunk.iteration}...`);
          break;
        }
        case "final_response":
          logEvent("final_response", `${(chunk.content ?? "").slice(0, 100)}...`);
          if (chunk.sessionId) setSessionId(chunk.sessionId);
          pendingMsg = {
            role: "agent",
            text: chunk.content ?? "(no response)",
            iterations: itersRef.length > 0 ? [...itersRef] : undefined,
            toolsUsed: itersRef.flatMap((i) => i.toolCalls.map((t) => t.name)),
          };
          setLiveStatus("Verifying...");
          break;
        case "verification":
          logEvent("verification", chunk.verification?.isVerified ? `Verified (${Math.round((chunk.verification.confidence ?? 0) * 100)}%)` : "Unverified");
          if (pendingMsg) pendingMsg.verification = chunk.verification;
          break;
        case "rule_suggestion":
          logEvent("rule_suggestion", `${chunk.followUpQuestions?.length ?? 0} suggestion(s)`);
          if (pendingMsg) pendingMsg.followUpQuestions = chunk.followUpQuestions;
          break;
        case "continuation_start":
          logEvent("continuation_start", `Window ${chunk.continuationWindow}`);
          setLiveStatus(`Continuing (window ${chunk.continuationWindow})...`);
          break;
        case "correction":
          logEvent("correction", chunk.content ?? "Self-correcting");
          setLiveStatus("Self-correcting...");
          break;
        case "hook_executed": {
          const parts = [chunk.hookName ?? "hook"];
          if (chunk.rulePackTriggeredCount !== undefined)
            parts.push(`triggered=${chunk.rulePackTriggeredCount}`);
          if (chunk.rulePackFilteredCount !== undefined)
            parts.push(`filtered=${chunk.rulePackFilteredCount}`);
          if (chunk.rulePackErrorAction)
            parts.push(`errorAction=${chunk.rulePackErrorAction}`);
          if (chunk.rulePackBlocked)
            parts.push("blocked=true");
          if ((chunk.rulePackTriggeredRules ?? []).length > 0)
            parts.push((chunk.rulePackTriggeredRules ?? []).join(", "));

          logEvent("hook_executed", parts.join(" | "), chunk.iteration);
          break;
        }
        case "error":
          logEvent("error", chunk.errorMessage ?? "Unknown error");
          pendingMsg = { role: "agent", text: chunk.errorMessage ?? "Unknown error", error: true };
          break;
        case "done":
          logEvent("done", chunk.executionTime ? `Completed in ${chunk.executionTime}` : "Done");
          if (chunk.sessionId) setSessionId(chunk.sessionId);
          if (pendingMsg) {
            if (pendingMsg.toolsUsed?.length === 0) delete pendingMsg.toolsUsed;
            pendingMsg.executionTime = chunk.executionTime;
          }
          break;
      }
    };

    try {
      await api.streamAgent(agent.id, query, sessionId, handleChunk, abort.signal, selectedModel || undefined, selectedConfigId);
      if (pendingMsg) {
        setMessages((m) => [...m, pendingMsg!]);
      }
    } catch (e: unknown) {
      if ((e as Error).name !== "AbortError") {
        setMessages((m) => [...m, { role: "agent", text: String(e), error: true }]);
      }
    } finally {
      setLoading(false);
      setLiveIterations([]);
      setLiveStatus("");
      setLivePlan(null);
    }
  };

  const renderContent = (message: Message) => {
    if (message.role !== "agent" || message.error) return message.text;
    const sanitized = DOMPurify.sanitize(message.text, { USE_PROFILES: { html: true } });
    return <div dangerouslySetInnerHTML={{ __html: sanitized }} />;
  };

  return (
    <div className="flex flex-col h-[calc(100vh-8rem)]">
      {/* Header */}
      <div className="flex items-center gap-3 mb-4 shrink-0">
        <Button variant="ghost" size="icon" onClick={() => navigate("/agents")}>
          <ArrowLeft className="size-4" />
        </Button>
        <div className="flex-1 min-w-0">
          <h1 className="text-lg font-semibold truncate">
            {agent ? (agent.displayName || agent.name) : "Loading..."}
          </h1>
          <p className="text-xs text-muted-foreground">
            {sessionId ? `Session: ${sessionId.slice(0, 8)}...` : "No active session"}
          </p>
        </div>
        <div className="flex items-center gap-3 shrink-0">
          {/* LLM Config picker — lets user test agent against different providers */}
          {availableLlmConfigs.length > 0 && (
            <Select
              value={selectedConfigId?.toString() ?? "__default__"}
              onValueChange={(v) => setSelectedConfigId(v === "__default__" ? undefined : parseInt(v))}
              disabled={loading}
            >
              <SelectTrigger className="w-44 h-8 text-xs">
                <SelectValue placeholder="Config" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="__default__" className="text-xs">Platform default</SelectItem>
                {availableLlmConfigs.map((c) => (
                  <SelectItem key={c.id} value={c.id.toString()} className="text-xs">
                    {c.displayName}{c.isRef ? " · via Platform" : ""}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
          <Select value={selectedModel || "__default__"} onValueChange={(v) => setSelectedModel(v === "__default__" ? "" : v)} disabled={loading}>
            <SelectTrigger className="w-52 h-8 text-xs">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="__default__">
                {llmConfig.defaultModel
                  ? `Default (${llmConfig.defaultModel})`
                  : "Default model"}
              </SelectItem>
              {llmConfig.availableModels.map((m) => (
                <SelectItem key={m} value={m} className="text-xs">{m}</SelectItem>
              ))}
            </SelectContent>
          </Select>
          <div className="flex items-center gap-1.5">
            <Switch id="detailed" checked={detailedMode} onCheckedChange={setDetailedMode} />
            <Label htmlFor="detailed" className="text-xs cursor-pointer">Detailed</Label>
          </div>
          <Button variant="ghost" size="icon" onClick={clearChat} title="Clear chat">
            <RotateCcw className="size-4" />
          </Button>
        </div>
      </div>

      <Separator className="mb-4 shrink-0" />

      {/* Messages */}
      <ScrollArea className="flex-1 pr-4">
        <div className="space-y-6 pb-4">
          {messages.length === 0 && !loading && (
            <div className="flex flex-col items-center justify-center pt-20 gap-3">
              <div className="rounded-full bg-muted p-4">
                <Bot className="size-8 text-muted-foreground" />
              </div>
              <p className="text-sm text-muted-foreground">Send a message to start the conversation</p>
            </div>
          )}

          {messages.map((m, i) => (
            <div key={i} className={cn("flex gap-2.5", m.role === "user" ? "flex-row-reverse" : "flex-row")}>
              <Avatar className="size-7 shrink-0 mt-0.5">
                <AvatarFallback className={m.role === "user" ? "bg-primary text-primary-foreground" : "bg-primary/10"}>
                  {m.role === "user"
                    ? <User className="size-3.5" />
                    : <Bot className="size-3.5 text-primary" />}
                </AvatarFallback>
              </Avatar>

              <div className={cn("flex flex-col max-w-[80%]", m.role === "user" ? "items-end" : "items-start")}>
                {/* Bubble */}
                <div className={cn(
                  "rounded-2xl px-4 py-3 text-sm leading-relaxed whitespace-pre-wrap",
                  m.role === "user"
                    ? "rounded-tr-sm bg-primary text-primary-foreground"
                    : m.error
                      ? "rounded-tl-sm bg-destructive/10 text-destructive border border-destructive/20"
                      : "rounded-tl-sm bg-muted"
                )}>
                  {renderContent(m)}
                </div>

                {/* Meta */}
                {m.role === "agent" && !m.error && (
                  <div className="flex flex-wrap items-center gap-1.5 mt-1.5">
                    {m.toolsUsed && m.toolsUsed.length > 0 && m.toolsUsed.map((t) => (
                      <Badge key={t} variant="secondary" className="text-[10px] h-4 px-1.5 font-mono">
                        <Wrench className="size-2.5 mr-1" />{t}
                      </Badge>
                    ))}
                    {m.executionTime && (
                      <span className="text-[10px] text-muted-foreground">{m.executionTime}</span>
                    )}
                    {m.verification && <VerificationBadge v={m.verification} />}
                  </div>
                )}

                {/* Iteration trace */}
                {m.role === "agent" && m.iterations && m.iterations.length > 0 && (
                  <IterationTrace iterations={m.iterations} detailed={detailedMode} />
                )}

                {/* Rule suggestions */}
                {m.role === "agent" && m.followUpQuestions?.filter((q) => q.type === "rule_confirmation").map((q, qi) => (
                  <div key={qi} className="mt-2 rounded-xl border border-indigo-500/30 bg-indigo-500/5 p-3 max-w-sm space-y-2">
                    <p className="text-xs text-indigo-400">{q.text}</p>
                    <div className="flex flex-wrap gap-1.5">
                      {q.options.map((opt) => (
                        <Button
                          key={opt}
                          variant="outline"
                          size="sm"
                          className="h-6 text-xs border-indigo-500/30"
                          onClick={() => setInput(opt)}
                        >
                          {opt}
                        </Button>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          ))}

          {/* Live feed */}
          {loading && (
            <LiveFeed
              iterations={liveIterations}
              status={liveStatus}
              plan={livePlan}
              timeline={liveTimeline}
              detailed={detailedMode}
            />
          )}

          <div ref={bottomRef} />
        </div>
      </ScrollArea>

      {/* Input */}
      <div className="mt-4 shrink-0">
        <Separator className="mb-4" />
        <div className="flex gap-3 items-end">
          <Textarea
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                send();
              }
            }}
            placeholder="Type a message... (Enter to send, Shift+Enter for new line)"
            disabled={loading}
            rows={3}
            className="resize-none flex-1"
          />
          <Button
            onClick={send}
            disabled={loading || !input.trim() || !agent}
            size="icon"
            className="h-[88px] w-11 shrink-0"
          >
            <Send className="size-4" />
          </Button>
        </div>
        <p className="text-[11px] text-muted-foreground mt-1.5">
          Enter to send · Shift+Enter for new line
          {loading && " · "}
          {loading && (
            <button
              className="underline text-muted-foreground hover:text-foreground"
              onClick={() => abortRef.current?.abort()}
            >
              Cancel
            </button>
          )}
        </p>
      </div>
    </div>
  );
}
