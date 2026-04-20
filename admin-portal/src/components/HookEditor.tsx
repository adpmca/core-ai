import { useEffect, useState } from "react";
import { Switch } from "@/components/ui/switch";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

interface KnownHook {
  syntheticKey: string;
  className: string;
  label: string;
  description: string;
  lifecyclePoint: string;
}

const KNOWN_HOOKS: KnownHook[] = [
  {
    syntheticKey: "__prompt_guard__",
    className: "PromptInjectionGuardHook",
    label: "Prompt Injection Guard",
    description: "Detects and blocks prompt injection patterns",
    lifecyclePoint: "On Init",
  },
  {
    syntheticKey: "__pii_redaction__",
    className: "PiiRedactionHook",
    label: "PII Redaction",
    description: "Redacts SSN, credit card, phone, and email from responses",
    lifecyclePoint: "Before Response",
  },
  {
    syntheticKey: "__citation_enforcer__",
    className: "CitationEnforcerHook",
    label: "Citation Enforcement",
    description: "Ensures responses cite their sources",
    lifecyclePoint: "Before Response",
  },
  {
    syntheticKey: "__disclaimer__",
    className: "DisclaimerAppenderHook",
    label: "Disclaimer Appender",
    description: "Appends a legal disclaimer to every response",
    lifecyclePoint: "Before Response",
  },
  {
    syntheticKey: "__length_guard__",
    className: "ResponseLengthGuardHook",
    label: "Response Length Guard",
    description: "Enforces maximum response length limit",
    lifecyclePoint: "Before Response",
  },
  {
    syntheticKey: "__audit_trail__",
    className: "AuditTrailHook",
    label: "Audit Trail",
    description: "Logs structured audit entries for compliance",
    lifecyclePoint: "After Response",
  },
];

function parseEnabledClasses(json: string | undefined): Set<string> {
  if (!json) return new Set();
  try {
    const obj = JSON.parse(json) as Record<string, string>;
    return new Set(Object.values(obj));
  } catch {
    return new Set();
  }
}

function serializeToggle(
  currentJson: string | undefined,
  hook: KnownHook,
  enabled: boolean
): string {
  let dict: Record<string, string> = {};
  try {
    if (currentJson) dict = JSON.parse(currentJson) as Record<string, string>;
  } catch {
    // start clean if JSON is corrupt
  }
  if (enabled) {
    dict[hook.syntheticKey] = hook.className;
  } else {
    // Remove by synthetic key and by class name value (handles archetype-sourced entries
    // that use hook point names as keys, e.g. "OnInit": "PromptInjectionGuardHook")
    delete dict[hook.syntheticKey];
    for (const key of Object.keys(dict)) {
      if (dict[key] === hook.className) delete dict[key];
    }
  }
  return JSON.stringify(dict);
}

interface HookEditorProps {
  value?: string;
  onChange: (json: string) => void;
}

export function HookEditor({ value, onChange }: HookEditorProps) {
  const [rawJson, setRawJson] = useState<string | undefined>(value);

  useEffect(() => { setRawJson(value); }, [value]);

  const enabledClasses = parseEnabledClasses(rawJson);

  const handleToggle = (hook: KnownHook, checked: boolean) => {
    const next = serializeToggle(rawJson, hook, checked);
    setRawJson(next);
    onChange(next);
  };

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="text-sm">Lifecycle Hooks</CardTitle>
        <CardDescription className="text-xs">
          Enable built-in behaviors that fire at specific agent lifecycle points.
        </CardDescription>
      </CardHeader>
      <CardContent className="divide-y divide-border">
        {KNOWN_HOOKS.map((hook) => (
          <div
            key={hook.syntheticKey}
            className="flex items-center justify-between gap-4 py-3 first:pt-0 last:pb-0"
          >
            <div className="flex items-center gap-3">
              <Switch
                id={hook.syntheticKey}
                checked={enabledClasses.has(hook.className)}
                onCheckedChange={(checked) => handleToggle(hook, checked)}
              />
              <div>
                <Label
                  htmlFor={hook.syntheticKey}
                  className="text-sm font-medium cursor-pointer"
                >
                  {hook.label}
                </Label>
                <p className="text-xs text-muted-foreground">{hook.description}</p>
              </div>
            </div>
            <Badge variant="secondary" className="shrink-0 text-xs font-normal">
              {hook.lifecyclePoint}
            </Badge>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}
