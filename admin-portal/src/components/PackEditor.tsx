import { useCallback, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router";
import {
  api,
  type AvailableLlmConfig,
  type BusinessRule,
  type ConflictAnalysis,
  type CreateHookRuleDto,
  type HookRule,
  type RulePack,
  type RulePackTestResult,
  type UpdateHookRuleDto,
  type UpdateRulePackDto,
} from "@/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Switch } from "@/components/ui/switch";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  AlertTriangle,
  ArrowLeft,
  Check,
  GripVertical,
  Info,
  Play,
  Plus,
  RefreshCw,
  Save,
  ShieldAlert,
  Trash2,
  X,
} from "lucide-react";
import {
  HookRuleForm,
  type HookRuleData,
  emptyHookRule,
  getValidRuleTypes,
  normalizeRuleForHookPoint,
  HOOK_POINT_BADGE_CLASS,
  RULE_TYPES,
} from "@/components/HookRuleForm";
import { toast } from "sonner";

const SEVERITY_ICON = {
  Info: <Info className="size-4 text-blue-500" />,
  Warning: <AlertTriangle className="size-4 text-amber-500" />,
  Error: <ShieldAlert className="size-4 text-red-500" />,
};

type RuleFormData = HookRuleData & { isEnabled?: boolean };

function emptyRule(): RuleFormData {
  return {
    ...emptyHookRule(0),
    hookPoint: "OnBeforeResponse",
    ruleType: "regex_redact",
  };
}

export function PackEditor() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const isNew = id === "new";

  const [pack, setPack] = useState<RulePack | null>(null);
  const [loading, setLoading] = useState(!isNew);

  // Pack metadata
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [version, setVersion] = useState("1.0");
  const [priority, setPriority] = useState(100);
  const [isEnabled, setIsEnabled] = useState(true);
  const [isMandatory, setIsMandatory] = useState(false);
  const [appliesToJson, setAppliesToJson] = useState("");
  const [activationCondition, setActivationCondition] = useState("");
  const [maxEvaluationMs, setMaxEvaluationMs] = useState(0);

  // Rule editor
  const [ruleDialog, setRuleDialog] = useState(false);
  const [editingRule, setEditingRule] = useState<RuleFormData>(emptyRule());
  const [availableLlmConfigs, setAvailableLlmConfigs] = useState<AvailableLlmConfig[]>([])

  // Linked business rules
  const [linkedRules, setLinkedRules] = useState<BusinessRule[]>([]);
  const [linkedRulesLoading, setLinkedRulesLoading] = useState(false);

  const loadLinkedRules = useCallback(async () => {
    if (!pack) return;
    setLinkedRulesLoading(true);
    try { setLinkedRules(await api.getBusinessRulesByPack(pack.id, 1)); }
    catch { /* non-critical */ }
    finally { setLinkedRulesLoading(false); }
  }, [pack]);

  useEffect(() => { loadLinkedRules(); }, [loadLinkedRules]);

  // Conflicts
  const [conflicts, setConflicts] = useState<ConflictAnalysis | null>(null);

  // Test
  const [testDialog, setTestDialog] = useState(false);
  const [testQuery, setTestQuery] = useState("sample query");
  const [testResponse, setTestResponse] = useState("sample response text");
  const [testResult, setTestResult] = useState<RulePackTestResult | null>(null);
  const [testing, setTesting] = useState(false);

  const loadPack = useCallback(async () => {
    if (isNew) return;
    setLoading(true);
    try {
      const p = await api.getRulePack(Number(id));
      setPack(p);
      setName(p.name);
      setDescription(p.description ?? "");
      setVersion(p.version);
      setPriority(p.priority);
      setIsEnabled(p.isEnabled);
      setIsMandatory(p.isMandatory);
      setAppliesToJson(p.appliesToJson ?? "");
      setActivationCondition(p.activationCondition ?? "");
      setMaxEvaluationMs(p.maxEvaluationMs);
    } catch {
      toast.error("Failed to load rule pack");
    }
    setLoading(false);
  }, [id, isNew]);

  useEffect(() => {
    loadPack();
  }, [loadPack]);

  useEffect(() => {
    api.listAvailableLlmConfigs().then(setAvailableLlmConfigs).catch(() => {});
  }, []);

  async function handleSavePack() {
    try {
      if (isNew) {
        const created = await api.createRulePack({
          name,
          description: description || undefined,
          priority,
          isMandatory,
          appliesToJson: appliesToJson || undefined,
          activationCondition: activationCondition || undefined,
          maxEvaluationMs,
        });
        toast.success("Rule pack created");
        navigate(`/rules/packs/${created.id}`, { replace: true });
      } else {
        const dto: UpdateRulePackDto = {
          name,
          description: description || undefined,
          version,
          priority,
          isEnabled,
          isMandatory,
          appliesToJson: appliesToJson || undefined,
          activationCondition: activationCondition || undefined,
          maxEvaluationMs,
        };
        await api.updateRulePack(Number(id), dto);
        toast.success("Rule pack saved");
        loadPack();
      }
    } catch {
      toast.error("Failed to save");
    }
  }

  function openRuleEditor(rule?: HookRule) {
    if (rule) {
      setEditingRule({
        id:              rule.id,
        hookPoint:       rule.hookPoint,
        ruleType:        rule.ruleType,
        pattern:         rule.pattern ?? "",
        instruction:     rule.instruction ?? "",
        replacement:     rule.replacement ?? "",
        toolName:        rule.toolName ?? "",
        orderInPack:     rule.orderInPack,
        stopOnMatch:     rule.stopOnMatch,
        isEnabled:       rule.isEnabled,
        maxEvaluationMs: rule.maxEvaluationMs,
        matchTarget:     (rule.matchTarget as "query" | "response") ?? "query",
      });
    } else {
      setEditingRule({
        ...emptyRule(),
        orderInPack: (pack?.rules.length ?? 0) + 1,
      });
    }
    setRuleDialog(true);
  }

  async function handleSaveRule() {
    if (!pack) return;
    const validRuleTypes = getValidRuleTypes(editingRule.hookPoint);
    if (!validRuleTypes.includes(editingRule.ruleType as (typeof RULE_TYPES)[number])) {
      toast.error(`Rule type '${editingRule.ruleType}' is not valid for ${editingRule.hookPoint}`);
      return;
    }
    try {
      if (editingRule.id) {
        const dto: UpdateHookRuleDto = {
          hookPoint: editingRule.hookPoint,
          ruleType: editingRule.ruleType,
          pattern: editingRule.pattern || undefined,
          instruction: editingRule.instruction || undefined,
          replacement: editingRule.replacement || undefined,
          toolName: editingRule.toolName || undefined,
          orderInPack: editingRule.orderInPack ?? 0,
          isEnabled: editingRule.isEnabled ?? true,
          stopOnMatch: editingRule.stopOnMatch ?? false,
          maxEvaluationMs: editingRule.maxEvaluationMs ?? 0,
          matchTarget: editingRule.matchTarget ?? "query",
        };
        await api.updateHookRule(pack.id, editingRule.id, dto);
        toast.success("Rule updated");
      } else {
        const dto: CreateHookRuleDto = {
          hookPoint: editingRule.hookPoint,
          ruleType: editingRule.ruleType,
          pattern: editingRule.pattern || undefined,
          instruction: editingRule.instruction || undefined,
          replacement: editingRule.replacement || undefined,
          toolName: editingRule.toolName || undefined,
          orderInPack: editingRule.orderInPack,
          stopOnMatch: editingRule.stopOnMatch,
          maxEvaluationMs: editingRule.maxEvaluationMs,
          matchTarget: editingRule.matchTarget ?? "query",
        };
        await api.addHookRule(pack.id, dto);
        toast.success("Rule added");
      }
      setRuleDialog(false);
      loadPack();
    } catch {
      toast.error("Failed to save rule");
    }
  }

  async function handleDeleteRule(ruleId: number) {
    if (!pack || !confirm("Delete this rule?")) return;
    await api.deleteHookRule(pack.id, ruleId);
    loadPack();
  }

  async function handleUnlinkBusinessRule(ruleId: number) {
    if (!confirm("Remove this business rule from the pack?")) return;
    try {
      await api.unassignBusinessRuleFromPack(ruleId, 1);
      loadLinkedRules();
    } catch {
      toast.error("Failed to unlink rule");
    }
  }

  async function loadConflicts() {
    if (!pack) return;
    try {
      const c = await api.analyzeConflicts(pack.id);
      setConflicts(c);
    } catch {
      toast.error("Failed to analyze conflicts");
    }
  }

  async function handleTest() {
    if (!pack) return;
    setTesting(true);
    try {
      const r = await api.testRulePack(pack.id, { sampleQuery: testQuery, sampleResponse: testResponse });
      setTestResult(r);
    } catch {
      toast.error("Test failed");
    }
    setTesting(false);
  }

  if (loading) return <div className="text-muted-foreground">Loading…</div>;

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center gap-3 flex-wrap">
        <Button variant="ghost" size="sm" onClick={() => navigate("/rules/packs")}>
          <ArrowLeft className="mr-1 size-4" /> Back
        </Button>
        <h1 className="text-2xl font-bold">{isNew ? "New Rule Pack" : pack?.name}</h1>
        {pack?.parentPackId && (
          <Badge variant="outline">Inherited from #{pack.parentPackId}</Badge>
        )}
        {!isNew && (
          <Badge variant="secondary">
            {pack?.appliesToJson ? `Applies to: ${pack.appliesToJson}` : "Applies to: All Agents"}
          </Badge>
        )}
        {!isNew && pack?.activationCondition && (
          <Badge variant="outline">
            Activates: {pack.activationCondition}
          </Badge>
        )}
      </div>

      {/* Pack Metadata */}
      <Card>
        <CardHeader>
          <CardTitle>Pack Details</CardTitle>
          <CardDescription>Configure the pack name, priority, and activation rules.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label>Name</Label>
              <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="PII Redaction Pack" />
            </div>
            <div className="space-y-1.5">
              <Label>Version</Label>
              <Input value={version} onChange={(e) => setVersion(e.target.value)} placeholder="1.0" disabled={isNew} />
            </div>
          </div>

          <div className="space-y-1.5">
            <Label>Description</Label>
            <Textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={2} placeholder="What this pack does…" />
          </div>

          <div className="space-y-3 rounded-md border border-blue-200 bg-blue-500/5 p-3 dark:border-blue-800">
            <div className="text-sm font-semibold text-blue-800 dark:text-blue-300">📋 Agent Assignment</div>

            <div className="space-y-1.5">
              <Label>Applies To Agents (archetype names)</Label>
              <div className="text-xs text-muted-foreground mb-2">
                Leave empty to apply to <strong>all agents</strong>, or specify agent types as JSON array (e.g., <code className="bg-muted px-1 py-0.5 rounded">["golf-ops","general"]</code>)
              </div>
              <Input
                value={appliesToJson}
                onChange={(e) => setAppliesToJson(e.target.value)}
                placeholder='Leave empty for all agents, or ["golf-ops","general"]'
                className="font-mono text-sm"
              />
              <div className="text-xs text-muted-foreground mt-1">
                💡 <strong>Tip:</strong> Empty = apply to all agents | Specific list = apply only to those archetypes
              </div>
            </div>

            <div className="space-y-1.5">
              <Label>Activation Condition (optional)</Label>
              <div className="text-xs text-muted-foreground mb-2">
                Only apply this pack when user query matches. Leave empty to always apply. Supports regex patterns.
              </div>
              <Input
                value={activationCondition}
                onChange={(e) => setActivationCondition(e.target.value)}
                placeholder="Examples: regex:(?i)revenue|forecast  OR  archetype:data-analyst"
                className="font-mono text-sm"
              />
              <div className="text-xs text-muted-foreground mt-1">
                Examples: <code className="bg-muted px-1 py-0.5 rounded">regex:(?i)revenue</code> or <code className="bg-muted px-1 py-0.5 rounded">archetype:data-analyst</code>
              </div>
            </div>
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label>Priority (lower runs first)</Label>
              <Input type="number" value={priority} onChange={(e) => setPriority(Number(e.target.value))} />
              <div className="text-xs text-muted-foreground">Default: 100. Multiple packs execute in priority order.</div>
            </div>
            <div className="space-y-1.5">
              <Label>Max Evaluation (ms, 0 = unlimited)</Label>
              <Input type="number" value={maxEvaluationMs} onChange={(e) => setMaxEvaluationMs(Number(e.target.value))} />
              <div className="text-xs text-muted-foreground">Timeout for all rules in this pack. Default: 500ms</div>
            </div>
          </div>

          <div className="flex gap-6">
            <label className="flex items-center gap-2">
              <Switch checked={isEnabled} onCheckedChange={setIsEnabled} disabled={isNew} />
              <span className="text-sm">Enabled</span>
            </label>
            <label className="flex items-center gap-2">
              <Switch checked={isMandatory} onCheckedChange={setIsMandatory} />
              <span className="text-sm">Mandatory (cannot be disabled by agents)</span>
            </label>
          </div>

          <Button onClick={handleSavePack} disabled={!name.trim()}>
            <Save className="mr-2 size-4" /> {isNew ? "Create Pack" : "Save Changes"}
          </Button>
        </CardContent>
      </Card>

      {/* Rules */}
      {!isNew && pack && (
        <Card>
          <CardHeader>
            <div className="flex items-center justify-between">
              <div>
                <CardTitle>Rules ({pack.rules.length})</CardTitle>
                <CardDescription>Hook rules executed in order across all supported lifecycle stages.</CardDescription>
              </div>
              <Button size="sm" onClick={() => openRuleEditor()}>
                <Plus className="mr-1 size-4" /> Add Rule
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            {pack.rules.length === 0 ? (
              <p className="text-sm text-muted-foreground">No rules yet. Add one to get started.</p>
            ) : (
              <div className="space-y-2">
                {[...pack.rules]
                  .sort((a, b) => a.orderInPack - b.orderInPack)
                  .map((rule) => (
                    <div
                      key={rule.id}
                      className="flex items-center gap-3 rounded-md border px-3 py-2 hover:bg-muted/50"
                    >
                      <GripVertical className="size-4 text-muted-foreground" />
                      <span className="w-6 text-center text-xs text-muted-foreground">
                        #{rule.orderInPack}
                      </span>
                      <Badge
                        variant="secondary"
                        className={HOOK_POINT_BADGE_CLASS[rule.hookPoint as keyof typeof HOOK_POINT_BADGE_CLASS] ?? "bg-muted text-muted-foreground"}
                      >
                        {rule.hookPoint}
                      </Badge>
                      <Badge variant="outline">{rule.ruleType}</Badge>
                      <span className="flex-1 truncate text-sm text-muted-foreground">
                        {rule.pattern || rule.instruction || rule.toolName || "—"}
                      </span>
                      {rule.stopOnMatch && (
                        <Badge variant="destructive" className="text-xs">Stop</Badge>
                      )}
                      {!rule.isEnabled && (
                        <Badge variant="secondary">Off</Badge>
                      )}
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => openRuleEditor(rule)}
                      >
                        Edit
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="text-destructive"
                        onClick={() => handleDeleteRule(rule.id)}
                      >
                        <Trash2 className="size-4" />
                      </Button>
                    </div>
                  ))}
              </div>
            )}
          </CardContent>
        </Card>
      )}

      {/* Conflict Analysis */}
      {!isNew && pack && (
        <Card>
          <CardHeader>
            <div className="flex items-center justify-between">
              <CardTitle>Conflict Analysis</CardTitle>
              <Button variant="outline" size="sm" onClick={loadConflicts}>
                <ShieldAlert className="mr-1 size-4" /> Analyze
              </Button>
            </div>
          </CardHeader>
          {conflicts && (
            <CardContent className="space-y-3">
              {conflicts.internal.length === 0 && conflicts.crossPack.length === 0 && (
                <div className="flex items-center gap-2 text-sm text-green-600">
                  <Check className="size-4" /> No conflicts detected
                </div>
              )}
              {conflicts.internal.length > 0 && (
                <div className="space-y-1">
                  <div className="text-sm font-medium">Internal</div>
                  {conflicts.internal.map((c, i) => (
                    <div key={i} className="flex items-start gap-2 text-sm">
                      {SEVERITY_ICON[c.severity]}
                      <span>{c.message}</span>
                    </div>
                  ))}
                </div>
              )}
              {conflicts.crossPack.length > 0 && (
                <div className="space-y-1">
                  <div className="text-sm font-medium">Cross-Pack</div>
                  {conflicts.crossPack.map((c, i) => (
                    <div key={i} className="flex items-start gap-2 text-sm">
                      {SEVERITY_ICON[c.severity]}
                      <span>{c.message}</span>
                    </div>
                  ))}
                </div>
              )}
            </CardContent>
          )}
        </Card>
      )}

      {/* Linked Business Rules */}
      {!isNew && pack && (
        <Card>
          <CardHeader>
            <div className="flex items-center justify-between">
              <div>
                <CardTitle>Linked Business Rules</CardTitle>
                <CardDescription>
                  Business rules assigned to this pack. They merge with the pack's hook rules at runtime.
                </CardDescription>
              </div>
              <div className="flex items-center gap-2">
                <Button variant="ghost" size="sm" onClick={loadLinkedRules} disabled={linkedRulesLoading}>
                  <RefreshCw className={`size-3.5 ${linkedRulesLoading ? "animate-spin" : ""}`} />
                </Button>
                <Button size="sm" onClick={() => navigate(`/rules/business/new?packId=${pack.id}&returnTo=/rules/packs/${pack.id}`)}>
                  <Plus className="mr-1 size-4" /> Add Business Rule
                </Button>
              </div>
            </div>
          </CardHeader>
          {linkedRules.length > 0 && (
            <CardContent>
              <div className="divide-y">
                {linkedRules.map(r => (
                  <div key={r.id} className="flex items-center justify-between py-2 text-sm">
                    <div className="flex-1 min-w-0">
                      <span className="font-mono text-xs text-muted-foreground mr-2">{r.ruleKey}</span>
                      <span className="text-foreground/80 line-clamp-1">{r.promptInjection}</span>
                    </div>
                    <div className="flex items-center gap-3 shrink-0 ml-4">
                      <span className="text-xs font-mono text-amber-400">{r.hookPoint}</span>
                      <span className="text-xs text-muted-foreground">order: {r.orderInPack}</span>
                      <Button variant="ghost" size="sm" className="h-6 px-2 text-xs"
                        onClick={() => navigate(`/rules/business/${r.id}/edit?returnTo=/rules/packs/${pack.id}`)}>
                        Edit
                      </Button>
                      <Button variant="ghost" size="sm" className="h-6 px-2 text-xs text-destructive hover:text-destructive"
                        onClick={() => handleUnlinkBusinessRule(r.id)}>
                        Unlink
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            </CardContent>
          )}
          {linkedRules.length === 0 && !linkedRulesLoading && (
            <CardContent>
              <p className="text-sm text-muted-foreground">No business rules linked to this pack. Click "Add Business Rule" to create one.</p>
            </CardContent>
          )}
        </Card>
      )}

      {/* Test / Dry Run */}
      {!isNew && pack && (
        <Card>
          <CardHeader>
            <div className="flex items-center justify-between">
              <div>
                <CardTitle>Dry Run Test</CardTitle>
                <CardDescription>Test this pack against sample input/output without side effects.</CardDescription>
              </div>
              <Button variant="outline" size="sm" onClick={() => setTestDialog(true)}>
                <Play className="mr-1 size-4" /> Test Pack
              </Button>
            </div>
          </CardHeader>
        </Card>
      )}

      {/* Business Rule Dialog — create/edit linked business rules */}
      {!isNew && pack && (
        <>
          {/* Linked rules are now edited via the /rules/business/:id/edit page */}
        </>
      )}

      {/* Rule Editor Dialog */}
      <Dialog open={ruleDialog} onOpenChange={setRuleDialog}>
        <DialogContent className="max-w-xl max-h-[85vh] overflow-y-auto">
          <DialogHeader>
            <DialogTitle>{editingRule.id ? "Edit Rule" : "Add Rule"}</DialogTitle>
          </DialogHeader>
          <div className="py-2">
            <HookRuleForm
              value={editingRule}
              onChange={(patch) => setEditingRule((r) =>
                "hookPoint" in patch
                  ? normalizeRuleForHookPoint({ ...r, ...patch } as RuleFormData, patch.hookPoint!)
                  : { ...r, ...patch }
              )}
              availableLlmConfigs={availableLlmConfigs}
            />
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setRuleDialog(false)}>Cancel</Button>
            <Button onClick={handleSaveRule}>
              {editingRule.id ? "Update Rule" : "Add Rule"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Test Dialog */}
      <Dialog open={testDialog} onOpenChange={(o) => { setTestDialog(o); if (!o) setTestResult(null); }}>
        <DialogContent className="max-w-xl">
          <DialogHeader>
            <DialogTitle>Dry Run Test</DialogTitle>
          </DialogHeader>
          <div className="space-y-4">
            <div className="space-y-1.5">
              <Label>Sample Query</Label>
              <Input value={testQuery} onChange={(e) => setTestQuery(e.target.value)} placeholder="Show me revenue for Q1 2026" />
            </div>
            <div className="space-y-1.5">
              <Label>Sample Response</Label>
              <Textarea value={testResponse} onChange={(e) => setTestResponse(e.target.value)} rows={4} />
            </div>
            <Button onClick={handleTest} disabled={testing}>
              <Play className="mr-1 size-4" /> {testing ? "Running…" : "Run Test"}
            </Button>

            {testResult && (
              <div className="space-y-3 rounded border p-3">
                {testResult.blocked ? (
                  <div className="flex items-center gap-2 text-sm font-semibold text-red-600">
                    <X className="size-4" /> Response was BLOCKED
                  </div>
                ) : (
                  <div className="flex items-center gap-2 text-sm text-green-600">
                    <Check className="size-4" /> Response passed
                  </div>
                )}
                <div className="space-y-1">
                  <div className="text-xs font-medium">Triggered Rules ({testResult.triggeredRules.length})</div>
                  {testResult.triggeredRules.map((r, i) => (
                    <div key={i} className="text-xs text-muted-foreground">
                      Rule #{r.ruleId}: {r.ruleType} → {r.action}
                    </div>
                  ))}
                  {testResult.triggeredRules.length === 0 && (
                    <div className="text-xs text-muted-foreground">No rules triggered</div>
                  )}
                </div>
                {testResult.modifiedResponse && testResult.modifiedResponse !== testResponse && (
                  <div className="space-y-1">
                    <div className="text-xs font-medium">Modified Response</div>
                    <pre className="max-h-40 overflow-auto rounded bg-muted p-2 text-xs">
                      {testResult.modifiedResponse}
                    </pre>
                  </div>
                )}
                {testResult.modelSwitchRequest && (
                  <div className="space-y-1">
                    <div className="text-xs font-medium text-sky-600 dark:text-sky-400">Model Switch Triggered</div>
                    <div className="text-xs text-muted-foreground">
                      {testResult.modelSwitchRequest.llmConfigId != null
                        ? <>LLM Config ID: <span className="font-mono">{testResult.modelSwitchRequest.llmConfigId}</span></>
                        : <>Model: <span className="font-mono">{testResult.modelSwitchRequest.modelId}</span></>}
                      {testResult.modelSwitchRequest.maxTokens != null && (
                        <>, Max Tokens: <span className="font-mono">{testResult.modelSwitchRequest.maxTokens}</span></>
                      )}
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setTestDialog(false)}>Close</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
