import { useState, useEffect, useCallback } from "react";
import {
  api, type AgentSummary, type ScheduledTask, type ScheduledTaskRun, type CreateScheduleDto,
} from "@/api";
import { toast } from "sonner";
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from "@/components/ui/table";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Switch } from "@/components/ui/switch";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogClose,
} from "@/components/ui/dialog";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription,
} from "@/components/ui/sheet";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem,
  DropdownMenuSeparator, DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent,
  AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { ScrollArea } from "@/components/ui/scroll-area";
import {
  MoreHorizontal, Plus, CalendarClock, Pencil, Trash2,
  Play, History, RefreshCw, ChevronDown, ChevronRight,
} from "lucide-react";

const TIMEZONES = [
  "UTC", "America/New_York", "America/Chicago", "America/Denver", "America/Los_Angeles",
  "America/Sao_Paulo", "Europe/London", "Europe/Paris", "Europe/Berlin", "Europe/Moscow",
  "Asia/Dubai", "Asia/Kolkata", "Asia/Singapore", "Asia/Tokyo", "Australia/Sydney",
];

const DAY_NAMES = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

function formatUtc(iso?: string) {
  if (!iso) return "—";
  return new Date(iso).toLocaleString();
}

function StatusBadge({ status }: { status: string }) {
  const colors: Record<string, string> = {
    running: "bg-blue-500/10 text-blue-400 border-blue-500/20",
    success: "bg-emerald-500/10 text-emerald-400 border-emerald-500/20",
    failed:  "bg-red-500/10 text-red-400 border-red-500/20",
    pending: "bg-amber-500/10 text-amber-400 border-amber-500/20",
    skipped: "bg-muted text-muted-foreground",
  };
  return (
    <Badge variant="outline" className={`text-xs font-medium ${colors[status] ?? "bg-muted text-muted-foreground"}`}>
      {status}
    </Badge>
  );
}

function scheduleLabel(task: ScheduledTask) {
  switch (task.scheduleType) {
    case "once":   return `Once · ${task.scheduledAtUtc ? formatUtc(task.scheduledAtUtc) : "—"}`;
    case "hourly": return "Hourly";
    case "daily":  return `Daily · ${task.runAtTime ?? ""}`;
    case "weekly": return `Weekly · ${DAY_NAMES[task.dayOfWeek ?? 0]} ${task.runAtTime ?? ""}`;
    default:       return task.scheduleType;
  }
}

// ── Main component ───────────────────────────────────────────────────────────

export function ScheduledTasks() {
  const [tasks, setTasks]       = useState<ScheduledTask[]>([]);
  const [agents, setAgents]     = useState<AgentSummary[]>([]);
  const [loading, setLoading]   = useState(true);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editTask, setEditTask] = useState<ScheduledTask | null>(null);
  const [runsTask, setRunsTask] = useState<ScheduledTask | null>(null);
  const [deleteId, setDeleteId] = useState<string | null>(null);

  useEffect(() => {
    api.listAgents().then(setAgents).catch(() => {});
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    try   { setTasks(await api.listSchedules(1)); }
    catch (e: unknown) { toast.error(String(e)); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { load(); }, [load]);

  const handleDelete = async () => {
    if (!deleteId) return;
    try {
      await api.deleteSchedule(deleteId, 1);
      setTasks(t => t.filter(x => x.id !== deleteId));
      toast.success("Schedule deleted.");
    } catch (e: unknown) { toast.error(String(e)); }
    finally { setDeleteId(null); }
  };

  const handleToggle = async (task: ScheduledTask) => {
    try {
      const updated = await api.setScheduleEnabled(task.id, !task.isEnabled, 1);
      setTasks(t => t.map(x => x.id === task.id ? updated : x));
    } catch (e: unknown) { toast.error(String(e)); }
  };

  const handleTrigger = async (task: ScheduledTask) => {
    try {
      await api.triggerSchedule(task.id, 1);
      toast.success(`Run queued for "${task.name}".`);
    } catch (e: unknown) { toast.error(String(e)); }
  };

  const agentName = (agentId: string) => {
    const a = agents.find(x => x.id === agentId);
    return a ? (a.displayName || a.name) : agentId;
  };

  const openCreate = () => { setEditTask(null); setDialogOpen(true); };
  const openEdit   = (task: ScheduledTask) => { setEditTask(task); setDialogOpen(true); };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-xl font-semibold">Scheduled Tasks</h2>
          <p className="text-sm text-muted-foreground">
            Schedule agents to run tasks on a recurring or one-time basis.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button size="sm" variant="outline" onClick={load} className="h-8">
            <RefreshCw className="h-3.5 w-3.5" />
          </Button>
          <Button size="sm" onClick={openCreate} className="h-8">
            <Plus className="h-3.5 w-3.5 mr-1" /> New Schedule
          </Button>
        </div>
      </div>

      {loading ? (
        <div className="rounded-md border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Agent</TableHead>
                <TableHead>Schedule</TableHead>
                <TableHead>Next Run</TableHead>
                <TableHead>Enabled</TableHead>
                <TableHead />
              </TableRow>
            </TableHeader>
            <TableBody>
              {Array.from({ length: 3 }).map((_, i) => (
                <TableRow key={i}>
                  <TableCell><Skeleton className="h-4 w-32" /></TableCell>
                  <TableCell><Skeleton className="h-4 w-24" /></TableCell>
                  <TableCell><Skeleton className="h-4 w-28" /></TableCell>
                  <TableCell><Skeleton className="h-4 w-28" /></TableCell>
                  <TableCell><Skeleton className="h-5 w-9" /></TableCell>
                  <TableCell><Skeleton className="h-5 w-5" /></TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      ) : tasks.length === 0 ? (
        <EmptyState
          icon={CalendarClock}
          title="No schedules"
          description="Create a schedule to automate agent tasks."
          action={{ label: "New Schedule", onClick: openCreate }}
        />
      ) : (
        <div className="rounded-md border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Agent</TableHead>
                <TableHead>Schedule</TableHead>
                <TableHead>Next Run</TableHead>
                <TableHead className="w-20 text-center">Enabled</TableHead>
                <TableHead className="w-10" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {tasks.map(task => (
                <TableRow key={task.id}>
                  <TableCell>
                    <div className="font-medium text-sm">{task.name}</div>
                    {task.description && (
                      <div className="text-xs text-muted-foreground mt-0.5 line-clamp-1">
                        {task.description}
                      </div>
                    )}
                  </TableCell>
                  <TableCell className="text-sm">{agentName(task.agentId)}</TableCell>
                  <TableCell>
                    <div className="text-xs text-foreground/80">{scheduleLabel(task)}</div>
                    <div className="text-xs text-muted-foreground/60">{task.timeZoneId}</div>
                  </TableCell>
                  <TableCell>
                    {task.isEnabled && task.nextRunUtc ? (
                      <span className="text-xs text-emerald-500">{formatUtc(task.nextRunUtc)}</span>
                    ) : (
                      <span className="text-xs text-muted-foreground">—</span>
                    )}
                  </TableCell>
                  <TableCell className="text-center">
                    <Switch
                      checked={task.isEnabled}
                      onCheckedChange={() => handleToggle(task)}
                      className="data-[state=checked]:bg-emerald-500"
                    />
                  </TableCell>
                  <TableCell>
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button variant="ghost" size="icon" className="h-7 w-7">
                          <MoreHorizontal className="h-4 w-4" />
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end">
                        <DropdownMenuItem onClick={() => openEdit(task)}>
                          <Pencil className="h-3.5 w-3.5 mr-2" /> Edit
                        </DropdownMenuItem>
                        <DropdownMenuItem onClick={() => handleTrigger(task)}>
                          <Play className="h-3.5 w-3.5 mr-2" /> Run Now
                        </DropdownMenuItem>
                        <DropdownMenuItem onClick={() => setRunsTask(task)}>
                          <History className="h-3.5 w-3.5 mr-2" /> Run History
                        </DropdownMenuItem>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem
                          onClick={() => setDeleteId(task.id)}
                          className="text-destructive focus:text-destructive"
                        >
                          <Trash2 className="h-3.5 w-3.5 mr-2" /> Delete
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      <TaskDialog
        open={dialogOpen}
        onOpenChange={setDialogOpen}
        initial={editTask}
        agents={agents}
        onSaved={() => { setDialogOpen(false); load(); }}
      />

      <RunHistorySheet
        task={runsTask}
        agentName={runsTask ? agentName(runsTask.agentId) : ""}
        onClose={() => setRunsTask(null)}
      />

      <AlertDialog open={deleteId !== null} onOpenChange={(open: boolean) => !open && setDeleteId(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete schedule?</AlertDialogTitle>
            <AlertDialogDescription>
              This schedule and all run history will be permanently removed.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              onClick={handleDelete}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              Delete
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

// ── Task form dialog ──────────────────────────────────────────────────────────

interface TaskDialogProps {
  open: boolean;
  onOpenChange: (v: boolean) => void;
  initial: ScheduledTask | null;
  agents: AgentSummary[];
  onSaved: () => void;
}

function TaskDialog({ open, onOpenChange, initial, agents, onSaved }: TaskDialogProps) {
  const editing = initial !== null;

  const [agentId,       setAgentId]       = useState(initial?.agentId ?? (agents[0]?.id ?? ""));
  const [name,          setName]          = useState(initial?.name ?? "");
  const [description,   setDescription]   = useState(initial?.description ?? "");
  const [scheduleType,  setScheduleType]  = useState(initial?.scheduleType ?? "once");
  const [scheduledAt,   setScheduledAt]   = useState(
    initial?.scheduledAtUtc ? initial.scheduledAtUtc.slice(0, 16) : "");
  const [runAtTime,     setRunAtTime]     = useState(initial?.runAtTime ?? "09:00");
  const [dayOfWeek,     setDayOfWeek]     = useState<number>(initial?.dayOfWeek ?? 1);
  const [timeZoneId,    setTimeZoneId]    = useState(initial?.timeZoneId ?? "UTC");
  const [payloadType,   setPayloadType]   = useState(initial?.payloadType ?? "prompt");
  const [promptText,    setPromptText]    = useState(initial?.promptText ?? "");
  const [parametersRaw, setParametersRaw] = useState<string>(
    initial?.parametersJson
      ? (() => { try { return JSON.stringify(JSON.parse(initial.parametersJson), null, 2); } catch { return initial.parametersJson; } })()
      : '{\n  "variable": "value"\n}'
  );
  const [isEnabled, setIsEnabled] = useState(initial?.isEnabled ?? true);
  const [saving,    setSaving]    = useState(false);

  useEffect(() => {
    if (open) {
      setAgentId(initial?.agentId ?? (agents[0]?.id ?? ""));
      setName(initial?.name ?? "");
      setDescription(initial?.description ?? "");
      setScheduleType(initial?.scheduleType ?? "once");
      setScheduledAt(initial?.scheduledAtUtc ? initial.scheduledAtUtc.slice(0, 16) : "");
      setRunAtTime(initial?.runAtTime ?? "09:00");
      setDayOfWeek(initial?.dayOfWeek ?? 1);
      setTimeZoneId(initial?.timeZoneId ?? "UTC");
      setPayloadType(initial?.payloadType ?? "prompt");
      setPromptText(initial?.promptText ?? "");
      setParametersRaw(
        initial?.parametersJson
          ? (() => { try { return JSON.stringify(JSON.parse(initial.parametersJson), null, 2); } catch { return initial.parametersJson; } })()
          : '{\n  "variable": "value"\n}'
      );
      setIsEnabled(initial?.isEnabled ?? true);
    }
  }, [open, initial, agents]);

  const save = async () => {
    if (!agentId)            { toast.error("Select an agent."); return; }
    if (!name.trim())        { toast.error("Name is required."); return; }
    if (!promptText.trim())  { toast.error("Prompt text is required."); return; }
    if (scheduleType === "once" && !scheduledAt) { toast.error("Select a date/time."); return; }

    let parsedParams: string | undefined;
    if (payloadType === "template") {
      try { JSON.parse(parametersRaw); parsedParams = parametersRaw; }
      catch { toast.error("Parameters JSON is not valid."); return; }
    }

    setSaving(true);
    try {
      const dto: CreateScheduleDto = {
        agentId,
        name:           name.trim(),
        description:    description.trim() || undefined,
        scheduleType,
        scheduledAtUtc: scheduleType === "once" ? new Date(scheduledAt).toISOString() : undefined,
        runAtTime:      (scheduleType === "daily" || scheduleType === "weekly") ? runAtTime : undefined,
        dayOfWeek:      scheduleType === "weekly" ? dayOfWeek : undefined,
        timeZoneId,
        payloadType,
        promptText:     promptText.trim(),
        parametersJson: parsedParams,
        isEnabled,
      };
      if (editing) {
        await api.updateSchedule(initial!.id, dto, 1);
      } else {
        await api.createSchedule(dto, 1);
      }
      toast.success(editing ? "Schedule updated." : "Schedule created.");
      onSaved();
    } catch (e: unknown) { toast.error(String(e)); }
    finally { setSaving(false); }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-2xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{editing ? "Edit Schedule" : "New Schedule"}</DialogTitle>
        </DialogHeader>

        <div className="grid gap-4 py-2">
          {/* Agent & name */}
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label>Agent *</Label>
              <Select value={agentId} onValueChange={setAgentId}>
                <SelectTrigger><SelectValue placeholder="Select agent" /></SelectTrigger>
                <SelectContent>
                  {agents.map(a => (
                    <SelectItem key={a.id} value={a.id}>{a.displayName || a.name}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label>Schedule Name *</Label>
              <Input value={name} onChange={e => setName(e.target.value)} placeholder="Daily report" />
            </div>
          </div>

          <div className="space-y-1.5">
            <Label>Description</Label>
            <Input
              value={description}
              onChange={e => setDescription(e.target.value)}
              placeholder="Optional description"
            />
          </div>

          {/* Schedule type */}
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <div className="space-y-1.5">
              <Label>Schedule Type</Label>
              <Select value={scheduleType} onValueChange={setScheduleType}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="once">Once</SelectItem>
                  <SelectItem value="hourly">Hourly</SelectItem>
                  <SelectItem value="daily">Daily</SelectItem>
                  <SelectItem value="weekly">Weekly</SelectItem>
                </SelectContent>
              </Select>
            </div>

            {scheduleType === "once" && (
              <div className="space-y-1.5 col-span-2">
                <Label>Run At (local) *</Label>
                <Input
                  type="datetime-local"
                  value={scheduledAt}
                  onChange={e => setScheduledAt(e.target.value)}
                />
              </div>
            )}

            {(scheduleType === "daily" || scheduleType === "weekly") && (
              <div className="space-y-1.5">
                <Label>Time of Day *</Label>
                <Input type="time" value={runAtTime} onChange={e => setRunAtTime(e.target.value)} />
              </div>
            )}

            {scheduleType === "weekly" && (
              <div className="space-y-1.5">
                <Label>Day of Week</Label>
                <Select value={String(dayOfWeek)} onValueChange={v => setDayOfWeek(Number(v))}>
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    {DAY_NAMES.map((d, i) => (
                      <SelectItem key={i} value={String(i)}>{d}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            )}

            <div className="space-y-1.5">
              <Label>Timezone</Label>
              <Select value={timeZoneId} onValueChange={setTimeZoneId}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  {TIMEZONES.map(tz => <SelectItem key={tz} value={tz}>{tz}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
          </div>

          {/* Payload */}
          <div className="space-y-1.5">
            <Label>Payload Type</Label>
            <Select value={payloadType} onValueChange={setPayloadType}>
              <SelectTrigger className="w-48"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="prompt">Fixed Prompt</SelectItem>
                <SelectItem value="template">Template (&#123;&#123;var&#125;&#125;)</SelectItem>
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-1.5">
            <Label>
              {payloadType === "template"
                ? "Prompt Template * (use {{variable}} for substitutions)"
                : "Prompt Text *"}
            </Label>
            <Textarea
              value={promptText}
              onChange={e => setPromptText(e.target.value)}
              rows={4}
              className="resize-y"
              placeholder={
                payloadType === "template"
                  ? "Generate a {{reportType}} summary for {{period}}."
                  : "Summarise today's key events and flag any anomalies."
              }
            />
          </div>

          {payloadType === "template" && (
            <div className="space-y-1.5">
              <Label>Template Parameters (JSON)</Label>
              <Textarea
                value={parametersRaw}
                onChange={e => setParametersRaw(e.target.value)}
                rows={4}
                className="font-mono text-sm resize-y"
                placeholder={'{\n  "reportType": "weekly"\n}'}
              />
            </div>
          )}

          <div className="flex items-center gap-2">
            <Switch id="task-enabled" checked={isEnabled} onCheckedChange={setIsEnabled} />
            <Label htmlFor="task-enabled">Enabled (run according to schedule)</Label>
          </div>
        </div>

        <DialogFooter>
          <DialogClose asChild>
            <Button variant="outline" disabled={saving}>Cancel</Button>
          </DialogClose>
          <Button onClick={save} disabled={saving}>
            {saving ? "Saving…" : editing ? "Save Changes" : "Create Schedule"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Run history sheet ─────────────────────────────────────────────────────────

interface RunHistorySheetProps {
  task: ScheduledTask | null;
  agentName: string;
  onClose: () => void;
}

function RunHistorySheet({ task, agentName, onClose }: RunHistorySheetProps) {
  const [runs, setRuns]       = useState<ScheduledTaskRun[]>([]);
  const [loading, setLoading] = useState(false);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());

  const load = useCallback(async () => {
    if (!task) return;
    setLoading(true);
    try   { setRuns(await api.getScheduleRuns(task.id, 1, 50)); }
    catch (e: unknown) { toast.error(String(e)); }
    finally { setLoading(false); }
  }, [task]);

  useEffect(() => { if (task) load(); }, [task, load]);

  const toggleExpand = (id: string) =>
    setExpanded(s => { const n = new Set(s); n.has(id) ? n.delete(id) : n.add(id); return n; });

  return (
    <Sheet open={task !== null} onOpenChange={(open: boolean) => !open && onClose()}>
      <SheetContent className="w-full sm:max-w-2xl overflow-y-auto">
        <SheetHeader className="mb-4">
          <SheetTitle>Run History — {task?.name}</SheetTitle>
          <SheetDescription>
            {agentName} · {task?.scheduleType} · {task?.timeZoneId}
          </SheetDescription>
        </SheetHeader>

        <div className="flex justify-end mb-3">
          <Button size="sm" variant="outline" onClick={load} disabled={loading} className="h-8">
            <RefreshCw className={`h-3.5 w-3.5 mr-1.5 ${loading ? "animate-spin" : ""}`} />
            Refresh
          </Button>
        </div>

        {loading ? (
          <div className="space-y-2">
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-14 w-full" />
            ))}
          </div>
        ) : runs.length === 0 ? (
          <p className="text-sm text-muted-foreground text-center py-12">No runs yet.</p>
        ) : (
          <ScrollArea className="h-[calc(100vh-220px)]">
            <div className="space-y-2 pr-1">
              {runs.map(run => (
                <div key={run.id} className="rounded-md border bg-card p-3">
                  <div className="flex items-center gap-2 flex-wrap">
                    <StatusBadge status={run.status} />
                    <span className="text-xs text-muted-foreground">
                      #{run.attemptNumber} · Due {formatUtc(run.scheduledForUtc)}
                    </span>
                    {run.startedAtUtc && (
                      <span className="text-xs text-muted-foreground">
                        Started {formatUtc(run.startedAtUtc)}
                      </span>
                    )}
                    {run.durationMs !== undefined && run.durationMs !== null && (
                      <span className="text-xs text-muted-foreground ml-auto">
                        {run.durationMs < 1000
                          ? `${run.durationMs}ms`
                          : `${(run.durationMs / 1000).toFixed(1)}s`}
                      </span>
                    )}
                    {(run.responseText || run.errorMessage) && (
                      <Button
                        variant="ghost"
                        size="sm"
                        className="h-6 px-2 text-xs ml-auto"
                        onClick={() => toggleExpand(run.id)}
                      >
                        {expanded.has(run.id)
                          ? <><ChevronDown className="h-3 w-3 mr-1" /> Collapse</>
                          : <><ChevronRight className="h-3 w-3 mr-1" /> Expand</>}
                      </Button>
                    )}
                  </div>

                  {expanded.has(run.id) && (
                    <div className="mt-2 space-y-2">
                      {run.errorMessage && (
                        <pre className="rounded bg-destructive/10 border border-destructive/20 text-destructive px-3 py-2 text-xs whitespace-pre-wrap break-words">
                          {run.errorMessage}
                        </pre>
                      )}
                      {run.responseText && (
                        <pre className="rounded bg-muted px-3 py-2 text-xs whitespace-pre-wrap break-words text-foreground">
                          {run.responseText}
                        </pre>
                      )}
                    </div>
                  )}
                </div>
              ))}
            </div>
          </ScrollArea>
        )}
      </SheetContent>
    </Sheet>
  );
}
