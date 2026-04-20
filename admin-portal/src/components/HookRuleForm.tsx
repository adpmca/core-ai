/**
 * Shared hook rule form — used in both PackEditor (rule pack hook rules) 
 * and BusinessRuleEditor (business rule hook configuration).
 *
 * Renders: hookPoint, ruleType, contextual help, pattern (with AI Builder),
 * instruction/text or model_switch config, replacement/tool name, order,
 * timeout, stopOnMatch, isEnabled.
 */
import { useCallback, useEffect, useState } from "react";
import { api, type AvailableLlmConfig } from "@/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Switch } from "@/components/ui/switch";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { Sparkles } from "lucide-react";
import { RegexAssistantDialog } from "@/components/RegexAssistantDialog";

// ── Constants ─────────────────────────────────────────────────────────────────

export const HOOK_POINTS = [
  "OnInit",
  "OnBeforeIteration",
  "OnToolFilter",
  "OnAfterToolCall",
  "OnBeforeResponse",
  "OnAfterResponse",
  "OnError",
] as const;

export const HOOK_POINT_BADGE_CLASS: Record<(typeof HOOK_POINTS)[number], string> = {
  OnInit:            "bg-blue-500/15 text-blue-700 dark:text-blue-400",
  OnBeforeIteration: "bg-amber-500/15 text-amber-700 dark:text-amber-400",
  OnToolFilter:      "bg-cyan-500/15 text-cyan-700 dark:text-cyan-400",
  OnAfterToolCall:   "bg-orange-500/15 text-orange-700 dark:text-orange-400",
  OnBeforeResponse:  "bg-green-500/15 text-green-700 dark:text-green-400",
  OnAfterResponse:   "bg-violet-500/15 text-violet-700 dark:text-violet-400",
  OnError:           "bg-rose-500/15 text-rose-700 dark:text-rose-400",
};

export const HOOK_POINT_HELP: Record<(typeof HOOK_POINTS)[number], string> = {
  OnInit:
    "Runs once before the ReAct loop starts. Best for prompt shaping and tool guidance.",
  OnBeforeIteration:
    "Runs at the start of each ReAct iteration. Best for iteration-specific prompt shaping and model switching. model_switch rules can match the user query or the previous iteration's response text. Also fires after a max_tokens truncation — use inject_prompt to add conciseness instructions on the follow-up turn.",
  OnToolFilter:
    "Runs after the LLM requests tools but before execution. Best for blocking or rewriting tool inputs.",
  OnAfterToolCall:
    "Runs after each tool returns. Best for redaction, normalization, or blocking tool output.",
  OnBeforeResponse:
    "Runs on the final response before verification and delivery.",
  OnAfterResponse:
    "Runs after the final response is emitted. This is a side-effect/audit stage; changes are stored as metadata, not sent back to the user.",
  OnError:
    "Runs when an LLM call, tool call, or max_tokens truncation occurs. Use block_pattern to abort (accept partial output) or tool_require to retry. Instruction text may contain 'abort', 'retry', or 'continue' to override the default action.",
};

export const RULE_TYPES = [
  "inject_prompt",
  "tool_require",
  "format_response",
  "format_enforce",
  "regex_redact",
  "append_text",
  "block_pattern",
  "require_keyword",
  "tool_transform",
  "model_switch",
] as const;

export const HOOK_POINT_RULE_TYPES: Record<
  (typeof HOOK_POINTS)[number],
  readonly (typeof RULE_TYPES)[number][]
> = {
  OnInit:            ["inject_prompt", "tool_require", "format_response", "tool_transform"],
  OnBeforeIteration: ["inject_prompt", "tool_require", "format_response", "tool_transform", "format_enforce", "model_switch"],
  OnToolFilter:      ["block_pattern", "tool_transform"],
  OnAfterToolCall:   ["regex_redact", "append_text", "block_pattern", "require_keyword", "format_enforce"],
  OnBeforeResponse:  ["regex_redact", "append_text", "block_pattern", "require_keyword", "format_enforce", "format_response"],
  OnAfterResponse:   ["append_text", "require_keyword", "format_response", "format_enforce"],
  OnError:           ["block_pattern", "tool_require"],
};

export const RULE_HELP: Record<string, string> = {
  inject_prompt:
    "Adds text to the system prompt. Put instruction text in 'Instruction'.",
  tool_require:
    "Requires a specific tool to be called. Set 'Tool Name' and optional 'Pattern' for query match.",
  format_response:
    "Wraps or reformats the final response. Set 'Pattern' (regex) and 'Replacement' ($1, $2…).",
  format_enforce:
    "Enforces a response format (e.g. markdown, JSON). Set 'Pattern' as the format name.",
  regex_redact:
    "Redacts matching text from the response. Set 'Pattern' (regex) and 'Replacement'.",
  append_text:
    "Appends fixed text to the response. Put the text in 'Instruction'.",
  block_pattern:
    "Blocks the response entirely if pattern matches. Set 'Pattern' (regex).",
  require_keyword:
    "Requires specific keywords in the response. Set 'Pattern' (comma-separated keywords).",
  tool_transform:
    "Transforms tool arguments before execution. Set 'Tool Name', 'Pattern', 'Replacement'.",
  model_switch:
    "Switches the LLM model for this iteration. 'Model ID' = same-provider model. 'LLM Config ID' = integer config ID for cross-provider switch (overrides Model ID). 'Max Tokens' = optional token cap. 'Pattern' = optional regex (blank = always trigger). 'Match Against' controls whether Pattern tests the user query or the previous iteration's response text.",
};

// ── Types ─────────────────────────────────────────────────────────────────────

/**
 * Data model for the hook rule form.
 * Maps to HookRule (in rule packs) and to the hook fields of BusinessRule.
 */
export interface HookRuleData {
  id?: number;
  hookPoint: string;
  ruleType: string;
  pattern?: string;
  /** Instruction text — maps to BusinessRule.promptInjection when used for business rules. */
  instruction?: string;
  replacement?: string;
  toolName?: string;
  orderInPack: number;
  stopOnMatch: boolean;
  maxEvaluationMs: number;
  isEnabled?: boolean;
  matchTarget?: "query" | "response";
}

export function emptyHookRule(orderInPack = 0): HookRuleData {
  return {
    hookPoint: "OnBeforeResponse",
    ruleType: "regex_redact",
    pattern: "",
    instruction: "",
    replacement: "",
    toolName: "",
    orderInPack,
    stopOnMatch: false,
    maxEvaluationMs: 0,
    matchTarget: "query",
  };
}

export function getValidRuleTypes(
  hookPoint: string
): readonly (typeof RULE_TYPES)[number][] {
  return (
    HOOK_POINT_RULE_TYPES[hookPoint as keyof typeof HOOK_POINT_RULE_TYPES] ??
    RULE_TYPES
  );
}

export function normalizeRuleForHookPoint(
  rule: HookRuleData,
  hookPoint: string
): HookRuleData {
  const valid = getValidRuleTypes(hookPoint);
  const ruleType = valid.includes(rule.ruleType as (typeof RULE_TYPES)[number])
    ? rule.ruleType
    : valid[0];
  return { ...rule, hookPoint, ruleType };
}

// ── Component ─────────────────────────────────────────────────────────────────

interface HookRuleFormProps {
  value: HookRuleData;
  onChange: (patch: Partial<HookRuleData>) => void;
  /**
   * When true, the 'Instruction / Text' field is hidden — useful when the
   * parent already shows a dedicated 'Prompt Injection' field for that content.
   */
  hideInstruction?: boolean;
  /** Pre-loaded LLM configs for the model_switch rule type. */
  availableLlmConfigs?: AvailableLlmConfig[];
}

export function HookRuleForm({
  value,
  onChange,
  hideInstruction = false,
  availableLlmConfigs: externalConfigs,
}: HookRuleFormProps) {
  const [regexOpen, setRegexOpen]           = useState(false);
  const [configModels, setConfigModels]      = useState<string[]>([]);
  const [ownConfigs, setOwnConfigs]          = useState<AvailableLlmConfig[]>([]);

  const configs = externalConfigs ?? ownConfigs;

  // Load LLM configs if not provided externally (lazy — only when model_switch is selected)
  useEffect(() => {
    if (externalConfigs !== undefined || value.ruleType !== "model_switch") return;
    api.listAvailableLlmConfigs().then(setOwnConfigs).catch(() => {});
  }, [value.ruleType, externalConfigs]);

  // Build the curated model list for a config — same source as AgentBuilder uses
  // (availableModels from the pre-loaded config data, not the live provider /v1/models API
  // which returns every model and overrides the admin-curated selection).
  const loadModelsForConfig = useCallback(
    (cfgId: string) => {
      const selectedCfg = configs.find((c) => c.id.toString() === cfgId);
      if (!selectedCfg) { setConfigModels([]); return; }
      const seen = new Set<string>();
      const models: string[] = [];
      const add = (m: string | undefined | null) => {
        if (m && !seen.has(m)) { seen.add(m); models.push(m); }
      };
      add(selectedCfg.model);
      selectedCfg.availableModels.forEach(add);
      setConfigModels(models);
    },
    [configs]
  );

  // Edit mode: configs load async — re-run when they arrive so the model list
  // for the already-selected config is populated.
  useEffect(() => {
    if (value.ruleType !== "model_switch" || !value.toolName || !configs.length) return;
    loadModelsForConfig(value.toolName);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value.ruleType, value.toolName, configs]);

  const allDbModels = useCallback(() => {
    const seen = new Set<string>();
    const models: string[] = [];
    for (const cfg of configs) {
      if (cfg.model && !seen.has(cfg.model)) { seen.add(cfg.model); models.push(cfg.model); }
      for (const m of cfg.availableModels) {
        if (!seen.has(m)) { seen.add(m); models.push(m); }
      }
    }
    return models;
  }, [configs]);

  const validRuleTypes = getValidRuleTypes(value.hookPoint);

  return (
    <div className="space-y-4">
      {/* Hook Point + Rule Type */}
      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-1.5">
          <Label>Hook Point</Label>
          <Select
            value={value.hookPoint}
            onValueChange={(v) => onChange(normalizeRuleForHookPoint(value, v))}
          >
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              {HOOK_POINTS.map((h) => (
                <SelectItem key={h} value={h}>{h}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1.5">
          <Label>Rule Type</Label>
          <Select
            value={value.ruleType}
            onValueChange={(v) => onChange({ ruleType: v })}
          >
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              {validRuleTypes.map((t) => (
                <SelectItem key={t} value={t}>{t}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>

      {/* Contextual help */}
      <p className="text-xs text-muted-foreground">
        {HOOK_POINT_HELP[value.hookPoint as keyof typeof HOOK_POINT_HELP]}
      </p>
      <p className="text-xs text-muted-foreground">
        {RULE_HELP[value.ruleType]}
      </p>

      {/* model_switch at OnBeforeIteration: Match Against select */}
      {value.ruleType === "model_switch" && value.hookPoint === "OnBeforeIteration" && (
        <div className="space-y-1.5">
          <Label>Match Pattern Against</Label>
          <Select
            value={value.matchTarget ?? "query"}
            onValueChange={(v) => onChange({ matchTarget: v as "query" | "response" })}
          >
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="query">User query (original request)</SelectItem>
              <SelectItem value="response">Previous iteration response</SelectItem>
            </SelectContent>
          </Select>
          <p className="text-xs text-muted-foreground">
            "Previous iteration response" matches the pattern against what the agent said in its last step — useful for switching model when the agent announces it will perform a specific action (e.g., "I will send the email").
          </p>
        </div>
      )}

      {/* Pattern with AI Builder */}
      <div className="space-y-1.5">
        <div className="flex items-center justify-between gap-2">
          <Label>
            {value.ruleType === "model_switch"
              ? value.matchTarget === "response"
                ? "Response Match Pattern (regex, optional — blank = always trigger)"
                : "Query Match Pattern (regex, optional — blank = always trigger)"
              : "Pattern (regex or match text)"}
          </Label>
          <Button
            variant="ghost"
            size="sm"
            className="h-6 px-2 gap-1 text-xs text-violet-600"
            type="button"
            onClick={() => setRegexOpen(true)}
            title="AI Regex Builder"
          >
            <Sparkles className="size-3" />
            AI Builder
          </Button>
        </div>
        <Input
          value={value.pattern ?? ""}
          onChange={(e) => onChange({ pattern: e.target.value })}
          className="font-mono text-sm"
          placeholder={
            value.ruleType === "model_switch"
              ? value.matchTarget === "response"
                ? "I will send|about to email|sending email"
                : "invoice|contract|legal"
              : "\\b\\d{3}-\\d{2}-\\d{4}\\b"
          }
        />
      </div>

      <RegexAssistantDialog
        open={regexOpen}
        onOpenChange={setRegexOpen}
        ruleType={value.ruleType}
        hookPoint={value.hookPoint}
        onApply={(pattern) => { onChange({ pattern }); setRegexOpen(false); }}
      />

      {/* model_switch: LLM Config + Model selects */}
      {value.ruleType === "model_switch" ? (
        <div className="grid gap-4 sm:grid-cols-2">
          <div className="space-y-1.5">
            <Label>LLM Config <span className="text-muted-foreground font-normal">(provider)</span></Label>
            <Select
              value={value.toolName || "__none__"}
              onValueChange={(v) => {
                const cfgId = v === "__none__" ? "" : v;
                onChange({ toolName: cfgId, instruction: "" });
                if (cfgId) loadModelsForConfig(cfgId);
                else setConfigModels([]);
              }}
            >
              <SelectTrigger><SelectValue placeholder="Select config…" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="__none__">— same provider, model only —</SelectItem>
                {configs.map((c) => (
                  <SelectItem key={c.id} value={c.id.toString()}>
                    {c.displayName}{c.provider ? ` · ${c.provider}` : ""}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1.5">
            <Label>Model</Label>
            <Select
              value={value.instruction || "__none__"}
              onValueChange={(v) => onChange({ instruction: v === "__none__" ? "" : v })}
            >
              <SelectTrigger className="font-mono text-sm">
                <SelectValue
                  placeholder={value.toolName ? "Select model…" : "Select a config first"}
                />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="__none__">— config default —</SelectItem>
                {(value.toolName ? configModels : allDbModels()).map((m) => (
                  <SelectItem key={m} value={m}>{m}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </div>
      ) : (
        !hideInstruction && (
          <div className="space-y-1.5">
            <Label>Instruction / Text</Label>
            <Textarea
              value={value.instruction ?? ""}
              onChange={(e) => onChange({ instruction: e.target.value })}
              rows={3}
              placeholder="Additional context or text to inject/append"
            />
          </div>
        )
      )}

      {/* Replacement + Tool Name */}
      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-1.5">
          <Label>
            {value.ruleType === "model_switch" ? "Max Tokens (optional)" : "Replacement"}
          </Label>
          <Input
            value={value.replacement ?? ""}
            onChange={(e) => onChange({ replacement: e.target.value })}
            placeholder={value.ruleType === "model_switch" ? "4096" : "[REDACTED]"}
            className="font-mono text-sm"
          />
        </div>
        {value.ruleType !== "model_switch" && (
          <div className="space-y-1.5">
            <Label>Tool Name</Label>
            <Input
              value={value.toolName ?? ""}
              onChange={(e) => onChange({ toolName: e.target.value })}
              placeholder="weather_get_forecast"
            />
          </div>
        )}
      </div>

      {/* Order + Timeout + StopOnMatch */}
      <div className="grid gap-4 sm:grid-cols-3">
        <div className="space-y-1.5">
          <Label>Order</Label>
          <Input
            type="number"
            value={value.orderInPack ?? 0}
            onChange={(e) => onChange({ orderInPack: Number(e.target.value) })}
          />
        </div>
        <div className="space-y-1.5">
          <Label>Timeout (ms)</Label>
          <Input
            type="number"
            value={value.maxEvaluationMs ?? 0}
            onChange={(e) => onChange({ maxEvaluationMs: Number(e.target.value) })}
          />
        </div>
        <div className="flex items-end gap-4 pb-1">
          <label className="flex items-center gap-2 cursor-pointer">
            <Switch
              checked={value.stopOnMatch ?? false}
              onCheckedChange={(v) => onChange({ stopOnMatch: v })}
            />
            <span className="text-sm">Stop on match</span>
          </label>
        </div>
      </div>

      {/* isEnabled toggle (optional — only shown for editing existing rules) */}
      {value.isEnabled !== undefined && (
        <label className="flex items-center gap-2 cursor-pointer">
          <Switch
            checked={value.isEnabled}
            onCheckedChange={(v) => onChange({ isEnabled: v })}
          />
          <span className="text-sm">Enabled</span>
        </label>
      )}
    </div>
  );
}
