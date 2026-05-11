import { useEffect, useState } from "react";
import { useParams } from "react-router";
import type { FewShotExample } from "@/api";
import {
  getFewShotExamples, addFewShotExample, deleteFewShotExample, reorderFewShotExamples,
} from "@/api";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Textarea } from "@/components/ui/textarea";

export default function AgentFewShotExamples()
{
  const { id: agentId } = useParams<{ id: string }>();
  const [examples, setExamples] = useState<FewShotExample[]>([]);
  const [showForm, setShowForm]   = useState(false);
  const [userMsg, setUserMsg]     = useState("");
  const [assistMsg, setAssistMsg] = useState("");
  const [desc, setDesc]           = useState("");
  const [saving, setSaving]       = useState(false);
  const [error, setError]         = useState<string | null>(null);

  const aid = agentId!;

  useEffect(() => { load(); }, [aid]);

  async function load()
  {
    setExamples(await getFewShotExamples(aid).catch(() => []));
  }

  async function handleAdd()
  {
    if (!userMsg.trim() || !assistMsg.trim()) return;
    setSaving(true);
    try
    {
      await addFewShotExample(aid, {
        agentId: aid,
        userMessage:      userMsg.trim(),
        assistantMessage: assistMsg.trim(),
        description:      desc.trim() || undefined,
        sortOrder:        examples.length,
        isEnabled:        true,
      });
      setUserMsg(""); setAssistMsg(""); setDesc(""); setShowForm(false);
      await load();
    }
    catch (e: unknown) { setError((e as { error?: string })?.error ?? "Failed to add example"); }
    finally { setSaving(false); }
  }

  async function handleDelete(id: number)
  {
    await deleteFewShotExample(aid, id).catch(e => setError(e?.error ?? "Failed"));
    await load();
  }

  async function move(index: number, dir: -1 | 1)
  {
    const next = index + dir;
    if (next < 0 || next >= examples.length) return;
    const updated = [...examples];
    [updated[index], updated[next]] = [updated[next], updated[index]];
    setExamples(updated);
    await reorderFewShotExamples(aid, updated.map(e => e.id)).catch(() => load());
  }

  const activeExamples = examples.filter(e => e.isEnabled);

  return (
    <div className="p-6 max-w-4xl mx-auto space-y-6">
      <div className="flex justify-between items-center">
        <h1 className="text-2xl font-bold">Few-Shot Examples</h1>
        <Button onClick={() => setShowForm(!showForm)} variant={showForm ? "secondary" : "default"}>
          {showForm ? "Cancel" : "+ Add Example"}
        </Button>
      </div>

      {error && (
        <div className="rounded-md border border-destructive/40 bg-destructive/10 text-destructive px-4 py-3 text-sm">
          {error}
        </div>
      )}

      {showForm && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">New Example</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            <div className="space-y-1">
              <label className="text-xs text-muted-foreground">User Message</label>
              <Textarea value={userMsg} onChange={e => setUserMsg(e.target.value)} rows={3}
                placeholder="User question…" />
            </div>
            <div className="space-y-1">
              <label className="text-xs text-muted-foreground">Assistant Message (ideal response)</label>
              <Textarea value={assistMsg} onChange={e => setAssistMsg(e.target.value)} rows={4}
                placeholder="Ideal response…" />
            </div>
            <div className="space-y-1">
              <label className="text-xs text-muted-foreground">Description (optional admin note)</label>
              <Input value={desc} onChange={e => setDesc(e.target.value)}
                placeholder="e.g. Good multi-part answer" />
            </div>
            <div className="flex gap-2">
              <Button onClick={handleAdd} disabled={saving}>
                {saving ? "Saving…" : "Save"}
              </Button>
              <Button variant="outline" onClick={() => setShowForm(false)}>Cancel</Button>
            </div>
          </CardContent>
        </Card>
      )}

      {examples.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          No examples yet. Add examples to inject into the agent's system prompt as response guides.
        </p>
      ) : (
        <div className="space-y-3">
          {examples.map((ex, i) => (
            <Card key={ex.id}>
              <CardContent className="pt-4 space-y-2">
                <div className="flex justify-between items-start gap-2">
                  <div className="flex items-center gap-2 flex-wrap">
                    <span className="text-xs text-muted-foreground">#{i + 1}</span>
                    {ex.description && <Badge variant="secondary">{ex.description}</Badge>}
                    {!ex.isEnabled && <Badge variant="destructive">Disabled</Badge>}
                    {ex.sourceSessionId && (
                      <span className="text-xs text-muted-foreground">From session</span>
                    )}
                  </div>
                  <div className="flex gap-1 shrink-0">
                    <Button size="sm" variant="outline" onClick={() => move(i, -1)} disabled={i === 0}>↑</Button>
                    <Button size="sm" variant="outline" onClick={() => move(i, 1)} disabled={i === examples.length - 1}>↓</Button>
                    <Button size="sm" variant="destructive" onClick={() => handleDelete(ex.id)}>Delete</Button>
                  </div>
                </div>
                <div className="text-xs text-muted-foreground font-semibold">User:</div>
                <div className="text-sm bg-muted rounded p-2">{ex.userMessage}</div>
                <div className="text-xs text-muted-foreground font-semibold">Assistant:</div>
                <div className="text-sm bg-muted/60 rounded p-2">{ex.assistantMessage}</div>
              </CardContent>
            </Card>
          ))}
        </div>
      )}

      {activeExamples.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">System Prompt Preview</CardTitle>
          </CardHeader>
          <CardContent>
            <pre className="text-xs whitespace-pre-wrap text-muted-foreground font-mono bg-muted rounded p-3">
              {"## Response Examples\n" + activeExamples.map(e =>
                `User: ${e.userMessage}\nAssistant: ${e.assistantMessage}`
              ).join("\n\n")}
            </pre>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
