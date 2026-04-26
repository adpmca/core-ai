import { useState, useEffect } from "react";
import { useNavigate } from "react-router";
import { api, type TenantGroup } from "@/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Switch } from "@/components/ui/switch";
import { Card, CardContent } from "@/components/ui/card";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Plus, Pencil, Trash2, Settings2 } from "lucide-react";
import { toast } from "sonner";

type CreateDto = { name: string; description: string };
const emptyCreate: CreateDto = { name: "", description: "" };

export function GroupList() {
  const navigate = useNavigate();
  const [groups,     setGroups]     = useState<TenantGroup[]>([]);
  const [loading,    setLoading]    = useState(true);
  const [createOpen, setCreateOpen] = useState(false);
  const [editGroup,  setEditGroup]  = useState<TenantGroup | null>(null);
  const [form,       setForm]       = useState<CreateDto>(emptyCreate);
  const [saving,     setSaving]     = useState(false);

  async function load() {
    try {
      setGroups(await api.listGroups());
    } catch (e) {
      toast.error(`Failed to load groups: ${e}`);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { load(); }, []);

  function openCreate() {
    setForm(emptyCreate);
    setCreateOpen(true);
  }

  function openEdit(g: TenantGroup) {
    setEditGroup(g);
    setForm({ name: g.name, description: g.description ?? "" });
  }

  async function create() {
    setSaving(true);
    try {
      await api.createGroup({ name: form.name, description: form.description || undefined });
      toast.success("Group created");
      setCreateOpen(false);
      load();
    } catch (e) {
      toast.error(`Failed: ${e}`);
    } finally {
      setSaving(false);
    }
  }

  async function update() {
    if (!editGroup) return;
    setSaving(true);
    try {
      await api.updateGroup(editGroup.id, {
        name: form.name,
        description: form.description || undefined,
        isActive: editGroup.isActive,
      });
      toast.success("Group updated");
      setEditGroup(null);
      load();
    } catch (e) {
      toast.error(`Failed: ${e}`);
    } finally {
      setSaving(false);
    }
  }

  async function toggleActive(g: TenantGroup) {
    try {
      await api.updateGroup(g.id, { name: g.name, description: g.description, isActive: !g.isActive });
      load();
    } catch (e) {
      toast.error(`Failed: ${e}`);
    }
  }

  async function deleteGroup(g: TenantGroup) {
    if (!confirm(`Delete group "${g.name}"? This cannot be undone.`)) return;
    try {
      await api.deleteGroup(g.id);
      toast.success("Deleted");
      load();
    } catch (e) {
      toast.error(`Failed: ${e}`);
    }
  }

  const f = (field: keyof CreateDto, v: string) =>
    setForm(prev => ({ ...prev, [field]: v }));

  return (
    <div className="p-6 space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold">Tenant Groups</h1>
          <p className="text-sm text-muted-foreground">Group tenants to share agents, rules, prompts, and LLM config.</p>
        </div>
        <Button onClick={openCreate}><Plus className="size-4 mr-2" /> New Group</Button>
      </div>

      <Card>
        <CardContent className="p-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-12">ID</TableHead>
                <TableHead>Name</TableHead>
                <TableHead>Description</TableHead>
                <TableHead>Members</TableHead>
                <TableHead>Active</TableHead>
                <TableHead>Created</TableHead>
                <TableHead className="w-32">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {loading ? (
                <TableRow><TableCell colSpan={7} className="text-center text-muted-foreground py-8">Loading…</TableCell></TableRow>
              ) : groups.length === 0 ? (
                <TableRow><TableCell colSpan={7} className="text-center text-muted-foreground py-8">No groups yet. Create one to get started.</TableCell></TableRow>
              ) : groups.map(g => (
                <TableRow key={g.id}>
                  <TableCell className="text-muted-foreground font-mono text-xs">{g.id}</TableCell>
                  <TableCell className="font-medium">{g.name}</TableCell>
                  <TableCell className="text-sm text-muted-foreground max-w-xs truncate">{g.description ?? "—"}</TableCell>
                  <TableCell>
                    <Badge variant="outline">{g.memberCount} tenant{g.memberCount !== 1 ? "s" : ""}</Badge>
                  </TableCell>
                  <TableCell>
                    <Switch checked={g.isActive} onCheckedChange={() => toggleActive(g)} />
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground">
                    {new Date(g.createdAt).toLocaleDateString()}
                  </TableCell>
                  <TableCell>
                    <div className="flex gap-1">
                      <Button size="icon" variant="ghost" title="Manage group"
                        onClick={() => navigate(`/platform/groups/${g.id}`)}>
                        <Settings2 className="size-4" />
                      </Button>
                      <Button size="icon" variant="ghost" onClick={() => openEdit(g)}>
                        <Pencil className="size-4" />
                      </Button>
                      <Button size="icon" variant="ghost" className="text-destructive"
                        onClick={() => deleteGroup(g)}>
                        <Trash2 className="size-4" />
                      </Button>
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>

      {/* Create dialog */}
      <Dialog open={createOpen} onOpenChange={setCreateOpen}>
        <DialogContent>
          <DialogHeader><DialogTitle>New Group</DialogTitle></DialogHeader>
          <div className="space-y-3 py-2">
            <div className="space-y-1">
              <Label>Group Name</Label>
              <Input value={form.name} onChange={e => f("name", e.target.value)} placeholder="e.g. Enterprise Accounts" />
            </div>
            <div className="space-y-1">
              <Label>Description <span className="text-muted-foreground text-xs">(optional)</span></Label>
              <Input value={form.description} onChange={e => f("description", e.target.value)} placeholder="Shared configuration for…" />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setCreateOpen(false)}>Cancel</Button>
            <Button onClick={create} disabled={saving || !form.name}>{saving ? "Creating…" : "Create"}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Edit dialog */}
      <Dialog open={!!editGroup} onOpenChange={v => { if (!v) setEditGroup(null); }}>
        <DialogContent>
          <DialogHeader><DialogTitle>Edit Group — {editGroup?.name}</DialogTitle></DialogHeader>
          <div className="space-y-3 py-2">
            <div className="space-y-1">
              <Label>Group Name</Label>
              <Input value={form.name} onChange={e => f("name", e.target.value)} placeholder="e.g. Enterprise Accounts" />
            </div>
            <div className="space-y-1">
              <Label>Description <span className="text-muted-foreground text-xs">(optional)</span></Label>
              <Input value={form.description} onChange={e => f("description", e.target.value)} placeholder="Shared configuration for…" />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setEditGroup(null)}>Cancel</Button>
            <Button onClick={update} disabled={saving || !form.name}>{saving ? "Saving…" : "Save"}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
