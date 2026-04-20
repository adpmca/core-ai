import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { toast } from "sonner";
import { ArrowLeft, Save, Trash2 } from "lucide-react";
import {
  api,
  type GroupAgentTemplate,
  type GroupAgentOverlay,
  type UpdateOverlayDto,
} from "@/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Switch } from "@/components/ui/switch";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Badge } from "@/components/ui/badge";

export function GroupAgentOverlayEditor() {
  const { templateId } = useParams<{ templateId: string }>();
  const navigate = useNavigate();

  const [template, setTemplate] = useState<GroupAgentTemplate | null>(null);
  const [overlay, setOverlay] = useState<GroupAgentOverlay | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [confirmRemove, setConfirmRemove] = useState(false);
  const [removing, setRemoving] = useState(false);

  // Editable overlay fields
  const [isEnabled, setIsEnabled] = useState(true);
  const [systemPromptAddendum, setSystemPromptAddendum] = useState("");
  const [modelId, setModelId] = useState("");
  const [temperature, setTemperature] = useState("");
  const [maxOutputTokens, setMaxOutputTokens] = useState("");

  useEffect(() => {
    if (!templateId) return;
    setLoading(true);
    Promise.all([
      api.getGroupTemplate(templateId),
      api.getOverlay(templateId).catch(() => null),
    ])
      .then(([tmpl, ov]) => {
        setTemplate(tmpl);
        setOverlay(ov);
        if (ov) {
          setIsEnabled(ov.isEnabled);
          setSystemPromptAddendum(ov.systemPromptAddendum ?? "");
          setModelId(ov.modelId ?? "");
          setTemperature(ov.temperature != null ? String(ov.temperature) : "");
          setMaxOutputTokens(ov.maxOutputTokens != null ? String(ov.maxOutputTokens) : "");
        }
      })
      .catch((e) => toast.error("Failed to load", { description: String(e) }))
      .finally(() => setLoading(false));
  }, [templateId]);

  const handleSave = async () => {
    if (!templateId) return;
    setSaving(true);
    try {
      const dto: UpdateOverlayDto = {
        isEnabled,
        systemPromptAddendum: systemPromptAddendum || undefined,
        modelId: modelId || undefined,
        temperature: temperature ? parseFloat(temperature) : undefined,
        maxOutputTokens: maxOutputTokens ? parseInt(maxOutputTokens, 10) : undefined,
        extraToolBindingsJson: overlay?.extraToolBindingsJson,
        customVariablesJson: overlay?.customVariablesJson,
        llmConfigId: overlay?.llmConfigId,
      };

      const updated = overlay
        ? await api.updateOverlay(templateId, dto)
        : await api.applyOverlay(templateId, dto);

      setOverlay(updated);
      toast.success("Overlay saved");
    } catch (e: unknown) {
      toast.error("Failed to save overlay", { description: String(e) });
    } finally {
      setSaving(false);
    }
  };

  const handleRemove = async () => {
    if (!templateId) return;
    setRemoving(true);
    try {
      await api.removeOverlay(templateId);
      toast.success("Customizations removed");
      navigate("/agents");
    } catch (e: unknown) {
      toast.error("Failed to remove overlay", { description: String(e) });
    } finally {
      setRemoving(false);
      setConfirmRemove(false);
    }
  };

  if (loading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-8 w-48" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  if (!template) {
    return (
      <div className="text-center py-12 text-muted-foreground">
        Template not found or you do not have access.
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-start justify-between">
        <div>
          <Button variant="ghost" size="sm" className="-ml-2 mb-1" onClick={() => navigate("/agents")}>
            <ArrowLeft className="mr-1.5 size-4" />
            Back to Agents
          </Button>
          <h1 className="text-2xl font-semibold tracking-tight">
            Customize: {template.displayName}
          </h1>
          <p className="text-sm text-muted-foreground">
            Adjust how this group template behaves for your tenant. Blank fields use the template default.
          </p>
        </div>
        <div className="flex gap-2">
          {overlay && (
            <Button variant="outline" onClick={() => setConfirmRemove(true)}>
              <Trash2 className="mr-1.5 size-4" />
              Remove Customizations
            </Button>
          )}
          <Button onClick={handleSave} disabled={saving}>
            <Save className="mr-1.5 size-4" />
            {saving ? "Saving…" : "Save"}
          </Button>
        </div>
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        {/* Left: template read-only info */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Template (read-only)</CardTitle>
            <CardDescription>Defined by your group admin — cannot be changed here.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3 text-sm">
            <div>
              <span className="font-medium">Agent Type:</span>{" "}
              <Badge variant="secondary">{template.agentType}</Badge>
            </div>
            {template.modelId && (
              <div>
                <span className="font-medium">Model:</span> {template.modelId}
              </div>
            )}
            <div>
              <span className="font-medium">Temperature:</span> {template.temperature}
            </div>
            {template.systemPrompt && (
              <div>
                <span className="font-medium block mb-1">System Prompt:</span>
                <pre className="text-xs whitespace-pre-wrap bg-muted p-3 rounded-md max-h-48 overflow-y-auto">
                  {template.systemPrompt}
                </pre>
              </div>
            )}
          </CardContent>
        </Card>

        {/* Right: editable overlay */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Your Customizations</CardTitle>
            <CardDescription>These override template defaults for your tenant only.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex items-center justify-between">
              <Label htmlFor="overlay-enabled">Active</Label>
              <Switch
                id="overlay-enabled"
                checked={isEnabled}
                onCheckedChange={setIsEnabled}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="model-override">Model Override</Label>
              <Input
                id="model-override"
                placeholder="Use template default"
                value={modelId}
                onChange={(e) => setModelId(e.target.value)}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="temperature-override">Temperature Override</Label>
              <Input
                id="temperature-override"
                type="number"
                step="0.1"
                min="0"
                max="2"
                placeholder="Use template default"
                value={temperature}
                onChange={(e) => setTemperature(e.target.value)}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="max-tokens-override">Max Output Tokens Override</Label>
              <Input
                id="max-tokens-override"
                type="number"
                placeholder="Use template default"
                value={maxOutputTokens}
                onChange={(e) => setMaxOutputTokens(e.target.value)}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="prompt-addendum">System Prompt Addendum</Label>
              <p className="text-xs text-muted-foreground">
                Appended after the template system prompt under a "Tenant Addendum" heading.
              </p>
              <Textarea
                id="prompt-addendum"
                placeholder="Optional extra instructions…"
                value={systemPromptAddendum}
                onChange={(e) => setSystemPromptAddendum(e.target.value)}
                rows={6}
              />
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Confirm removal dialog */}
      <Dialog open={confirmRemove} onOpenChange={(open) => !open && setConfirmRemove(false)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Remove Customizations</DialogTitle>
            <DialogDescription>
              This will remove your overlay for <strong>{template.displayName}</strong>. The agent will
              no longer appear in your registry. The group template remains unchanged.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirmRemove(false)}>Cancel</Button>
            <Button variant="destructive" onClick={handleRemove} disabled={removing}>
              {removing ? "Removing…" : "Remove"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
