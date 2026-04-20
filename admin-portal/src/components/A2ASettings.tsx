import { useCallback, useEffect, useState } from "react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { Globe, Shield, Timer, Layers, Gauge, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { api, type A2AConfig } from "@/api";

export function A2ASettings() {
  const [config, setConfig] = useState<A2AConfig | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setConfig(await api.getA2AConfig());
    } catch {
      toast.error("Failed to load A2A configuration");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  if (loading) {
    return (
      <div className="space-y-4 p-6">
        <Skeleton className="h-8 w-48" />
        <Skeleton className="h-40 w-full" />
        <Skeleton className="h-40 w-full" />
      </div>
    );
  }

  if (!config) {
    return (
      <div className="p-6 text-muted-foreground">
        Unable to load A2A configuration.
      </div>
    );
  }

  const items: { icon: React.ElementType; label: string; value: string | number; description: string }[] = [
    { icon: Globe, label: "Base URL", value: config.baseUrl ?? "(auto-detected)", description: "Public URL used for AgentCard discovery" },
    { icon: Timer, label: "Task Timeout", value: `${config.taskTimeoutSeconds}s`, description: "Max seconds before a running task is failed" },
    { icon: Shield, label: "Max Delegation Depth", value: config.maxDelegationDepth, description: "Prevents infinite A2A call loops" },
    { icon: Layers, label: "Max Concurrent Tasks", value: config.maxConcurrentTasks === 0 ? "Unlimited" : config.maxConcurrentTasks, description: "Concurrent inbound A2A tasks allowed" },
    { icon: Gauge, label: "Rate Limit", value: `${config.rateLimitPerMinute} req/min`, description: "Sliding window rate limit on /tasks/* endpoints" },
    { icon: Trash2, label: "Task Retention", value: `${config.taskRetentionDays} days`, description: "Completed/failed tasks are auto-purged after this period" },
  ];

  return (
    <div className="space-y-6 p-6">
      <div className="flex items-center gap-3">
        <h1 className="text-2xl font-bold tracking-tight">A2A Protocol Settings</h1>
        <Badge variant={config.enabled ? "default" : "secondary"}>
          {config.enabled ? "Enabled" : "Disabled"}
        </Badge>
      </div>

      <p className="text-sm text-muted-foreground">
        Agent-to-Agent protocol configuration. These settings are defined in <code>appsettings.json</code> under the <code>A2A</code> section
        and apply platform-wide.
      </p>

      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        {items.map((item) => (
          <Card key={item.label}>
            <CardHeader className="pb-2">
              <CardTitle className="flex items-center gap-2 text-sm font-medium">
                <item.icon className="size-4 text-muted-foreground" />
                {item.label}
              </CardTitle>
              <CardDescription className="text-xs">{item.description}</CardDescription>
            </CardHeader>
            <CardContent>
              <span className="text-2xl font-bold">{item.value}</span>
            </CardContent>
          </Card>
        ))}
      </div>
    </div>
  );
}
