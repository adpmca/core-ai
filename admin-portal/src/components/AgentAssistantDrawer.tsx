import { useState, useEffect } from "react";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription } from "@/components/ui/sheet";
import { Textarea } from "@/components/ui/textarea";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Loader2, Sparkles, ChevronRight, Check, AlertTriangle, RefreshCw, Bot } from "lucide-react";
import {
  api,
  type AgentSetupContext,
  type PromptSuggestion,
  type SuggestedRulePack,
  type AgentSummary,
} from "@/api";

// ── Types ─────────────────────────────────────────────────────────────────────

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  agentId?: string;
  agentName?: string;
  agentDescription?: string;
  archetypeId?: string;
  toolNames?: string[];
  delegateAgentIds?: string[];
  currentSystemPrompt?: string;
  currentRulePacksJson?: string;
  onApplyPrompt?: (prompt: string) => void;
  onApplyRulePacks?: (packs: SuggestedRulePack[]) => void;
}

type Step = "context" | "prompt" | "rule-packs";

// ── Component ─────────────────────────────────────────────────────────────────

export function AgentAssistantDrawer({
  open,
  onOpenChange,
  agentId,
  agentName = "",
  agentDescription: initialDescription = "",
  archetypeId,
  toolNames = [],
  delegateAgentIds = [],
  currentSystemPrompt,
  currentRulePacksJson,
  onApplyPrompt,
  onApplyRulePacks,
}: Props) {
  const [step, setStep] = useState<Step>("context");
  const [mode, setMode] = useState<"create" | "refine">(currentSystemPrompt ? "refine" : "create");
  const [description, setDescription] = useState(initialDescription);
  const [additionalContext, setAdditionalContext] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [delegateAgents, setDelegateAgents] = useState<AgentSummary[]>([]);

  // Fetch agent names for display when delegate IDs are present
  useEffect(() => {
    if (delegateAgentIds.length === 0) { setDelegateAgents([]); return; }
    api.listAgents().then(agents => {
      setDelegateAgents(agents.filter(a => delegateAgentIds.includes(a.id)));
    }).catch(() => {});
  }, [delegateAgentIds.join(",")]); // eslint-disable-line react-hooks/exhaustive-deps

  const [promptSuggestion, setPromptSuggestion] = useState<PromptSuggestion | null>(null);
  const [editedPrompt, setEditedPrompt] = useState("");
  const [packSuggestions, setPackSuggestions] = useState<SuggestedRulePack[]>([]);
  const [selectedPacks, setSelectedPacks] = useState<Set<number>>(new Set());

  const buildContext = (): AgentSetupContext => ({
    agentName,
    agentDescription: description.slice(0, 500),
    archetypeId,
    toolNames,
    additionalContext: additionalContext.slice(0, 300),
    mode,
    agentId,
    delegateAgentIds: delegateAgentIds && delegateAgentIds.length > 0 ? delegateAgentIds : undefined,
    currentSystemPrompt: mode === "refine" ? currentSystemPrompt : undefined,
    currentRulePacksJson: mode === "refine" ? currentRulePacksJson : undefined,
  });

  const handleSuggestPrompt = async () => {
    setLoading(true);
    setError(null);
    try {
      const ctx = buildContext();
      const result = await api.suggestPrompt(ctx);
      setPromptSuggestion(result);
      setEditedPrompt(result.systemPrompt);
      setStep("prompt");
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "Prompt suggestion failed.");
    } finally {
      setLoading(false);
    }
  };

  const handleSuggestRulePacks = async () => {
    setLoading(true);
    setError(null);
    try {
      const ctx = buildContext();
      const result = await api.suggestRulePacks(ctx);
      setPackSuggestions(result);
      setSelectedPacks(new Set(result.map((_, i) => i).filter(i => result[i].operation !== "keep")));
      setStep("rule-packs");
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "Rule pack suggestion failed.");
    } finally {
      setLoading(false);
    }
  };

  const handleApplyPrompt = () => {
    if (editedPrompt.trim()) onApplyPrompt?.(editedPrompt.trim());
  };

  const handleApplyRulePacks = () => {
    const selected = packSuggestions.filter((_, i) => selectedPacks.has(i));
    onApplyRulePacks?.(selected);
  };

  const togglePack = (i: number) => {
    const next = new Set(selectedPacks);
    if (next.has(i)) next.delete(i); else next.add(i);
    setSelectedPacks(next);
  };

  const reset = () => {
    setStep("context");
    setPromptSuggestion(null);
    setEditedPrompt("");
    setPackSuggestions([]);
    setSelectedPacks(new Set());
    setError(null);
  };

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent className="w-full sm:max-w-2xl overflow-y-auto flex flex-col gap-6">
        <SheetHeader>
          <SheetTitle className="flex items-center gap-2">
            <Sparkles className="w-5 h-5 text-violet-500" />
            AI Agent Setup Assistant
          </SheetTitle>
          <SheetDescription>
            {step === "context" && "Describe what this agent should do and get AI-generated suggestions."}
            {step === "prompt" && "Review and edit the suggested system prompt before applying."}
            {step === "rule-packs" && "Review suggested rule packs. Select the ones to apply."}
          </SheetDescription>
        </SheetHeader>

        {/* Step indicator */}
        <div className="flex items-center gap-2 text-sm text-muted-foreground">
          <span className={step === "context" ? "font-semibold text-foreground" : ""}>1 Context</span>
          <ChevronRight className="w-4 h-4" />
          <span className={step === "prompt" ? "font-semibold text-foreground" : ""}>2 System Prompt</span>
          <ChevronRight className="w-4 h-4" />
          <span className={step === "rule-packs" ? "font-semibold text-foreground" : ""}>3 Rule Packs</span>
        </div>

        {error && (
          <Alert variant="destructive">
            <AlertTriangle className="h-4 w-4" />
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}

        {/* ── Step 1: Context ── */}
        {step === "context" && (
          <div className="flex flex-col gap-4">
            <div className="flex gap-3">
              <Button
                variant={mode === "create" ? "default" : "outline"}
                size="sm"
                onClick={() => setMode("create")}
              >
                Create New
              </Button>
              <Button
                variant={mode === "refine" ? "default" : "outline"}
                size="sm"
                onClick={() => setMode("refine")}
                disabled={!currentSystemPrompt}
              >
                Refine Existing
              </Button>
            </div>

            <div className="space-y-1">
              <Label>Agent Name</Label>
              <Input value={agentName} disabled className="bg-muted" />
            </div>

            <div className="space-y-1">
              <Label>Description *</Label>
              <Textarea
                placeholder="What does this agent do? What business problem does it solve?"
                value={description}
                onChange={e => setDescription(e.target.value.slice(0, 500))}
                rows={4}
              />
              <p className="text-xs text-muted-foreground text-right">{description.length}/500</p>
            </div>

            {toolNames.length > 0 && (
              <div className="space-y-1">
                <Label>Available Tools</Label>
                <div className="flex flex-wrap gap-1">
                  {toolNames.map(t => <Badge key={t} variant="secondary">{t}</Badge>)}
                </div>
              </div>
            )}

            {delegateAgents.length > 0 && (
              <div className="space-y-1">
                <Label>Delegate Sub-Agents</Label>
                <div className="flex flex-wrap gap-1">
                  {delegateAgents.map(a => (
                    <Badge key={a.id} variant="outline" className="gap-1">
                      <Bot className="size-3" />{a.displayName}
                    </Badge>
                  ))}
                </div>
              </div>
            )}

            {!agentId && (toolNames.length > 0 || (delegateAgentIds && delegateAgentIds.length > 0)) && (
              <p className="text-xs text-muted-foreground">
                Save the agent first to enable tool function discovery in prompt generation.
              </p>
            )}

            {archetypeId && (
              <div className="space-y-1">
                <Label>Archetype</Label>
                <Badge variant="outline">{archetypeId}</Badge>
              </div>
            )}

            <div className="space-y-1">
              <Label>Additional Context (optional)</Label>
              <Textarea
                placeholder="Any special constraints, tone, industry, or requirements?"
                value={additionalContext}
                onChange={e => setAdditionalContext(e.target.value.slice(0, 300))}
                rows={3}
              />
              <p className="text-xs text-muted-foreground text-right">{additionalContext.length}/300</p>
            </div>

            <div className="flex gap-3">
              <Button
                onClick={handleSuggestPrompt}
                disabled={loading || !description.trim()}
                className="flex-1"
              >
                {loading ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : <Sparkles className="w-4 h-4 mr-2" />}
                Suggest System Prompt
              </Button>
              <Button
                variant="outline"
                onClick={handleSuggestRulePacks}
                disabled={loading || !description.trim()}
                className="flex-1"
              >
                {loading ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : <Sparkles className="w-4 h-4 mr-2" />}
                Suggest Rule Packs
              </Button>
            </div>
          </div>
        )}

        {/* ── Step 2: System Prompt ── */}
        {step === "prompt" && promptSuggestion && (
          <div className="flex flex-col gap-4">
            {promptSuggestion.rationale && (
              <Alert>
                <AlertDescription className="text-sm">{promptSuggestion.rationale}</AlertDescription>
              </Alert>
            )}
            <div className="space-y-1">
              <Label>Suggested System Prompt</Label>
              <Textarea
                value={editedPrompt}
                onChange={e => setEditedPrompt(e.target.value)}
                rows={16}
                className="font-mono text-sm"
              />
            </div>
            <div className="flex gap-3">
              <Button
                variant="ghost"
                onClick={reset}
              >
                <RefreshCw className="w-4 h-4 mr-2" />
                Start Over
              </Button>
              <Button
                variant="outline"
                onClick={handleSuggestPrompt}
                disabled={loading}
              >
                {loading ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : <RefreshCw className="w-4 h-4 mr-2" />}
                Regenerate
              </Button>
              <Button
                onClick={() => { handleApplyPrompt(); handleSuggestRulePacks(); }}
                disabled={loading || !editedPrompt.trim()}
                className="flex-1"
              >
                <Check className="w-4 h-4 mr-2" />
                Apply & Continue to Rule Packs
              </Button>
              <Button
                variant="outline"
                onClick={handleApplyPrompt}
                disabled={!editedPrompt.trim()}
              >
                Apply Only
              </Button>
            </div>
          </div>
        )}

        {/* ── Step 3: Rule Packs ── */}
        {step === "rule-packs" && (
          <div className="flex flex-col gap-4">
            {packSuggestions.length === 0 && (
              <p className="text-muted-foreground text-sm">No rule pack suggestions were generated. Try adjusting the description or regenerating.</p>
            )}
            {packSuggestions.map((pack, i) => (
              <div
                key={i}
                className={`border rounded-lg p-4 cursor-pointer transition-colors ${selectedPacks.has(i) ? "border-violet-500 bg-violet-50 dark:bg-violet-950/20" : "border-border"}`}
                onClick={() => togglePack(i)}
              >
                <div className="flex items-start justify-between gap-2">
                  <div className="flex-1">
                    <div className="flex items-center gap-2">
                      <span className="font-medium text-sm">{pack.name}</span>
                      <Badge variant="outline" className="text-xs capitalize">{pack.operation}</Badge>
                    </div>
                    <p className="text-xs text-muted-foreground mt-1">{pack.description}</p>
                    {pack.rationale && (
                      <p className="text-xs text-muted-foreground mt-1 italic">{pack.rationale}</p>
                    )}
                    <div className="flex flex-wrap gap-1 mt-2">
                      {pack.rules.map((rule, j) => (
                        <Badge key={j} variant="secondary" className="text-xs">
                          {rule.hookPoint} / {rule.ruleType}
                        </Badge>
                      ))}
                    </div>
                  </div>
                  <div className={`w-5 h-5 rounded border flex items-center justify-center flex-shrink-0 ${selectedPacks.has(i) ? "bg-violet-500 border-violet-500" : "border-border"}`}>
                    {selectedPacks.has(i) && <Check className="w-3 h-3 text-white" />}
                  </div>
                </div>
              </div>
            ))}
            <div className="flex gap-3">
              <Button variant="ghost" onClick={() => setStep("prompt")}>
                ← Back
              </Button>
              <Button
                variant="outline"
                onClick={handleSuggestRulePacks}
                disabled={loading}
              >
                {loading ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : <RefreshCw className="w-4 h-4 mr-2" />}
                Regenerate
              </Button>
              <Button
                onClick={() => { handleApplyRulePacks(); onOpenChange(false); }}
                disabled={selectedPacks.size === 0}
                className="flex-1"
              >
                <Check className="w-4 h-4 mr-2" />
                Apply {selectedPacks.size > 0 ? `${selectedPacks.size} Pack(s)` : ""}
              </Button>
            </div>
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
