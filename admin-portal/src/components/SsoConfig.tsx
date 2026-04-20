import { useState, useEffect } from "react";
import { useNavigate } from "react-router";
import { api, type SsoConfig } from "@/api";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Switch } from "@/components/ui/switch";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Plus, Pencil, Trash2, Shield } from "lucide-react";
import { toast } from "sonner";

export function SsoConfig({ tenantId = 1 }: { tenantId?: number }) {
  const navigate = useNavigate();
  const [configs, setConfigs] = useState<SsoConfig[]>([]);
  const [loading, setLoading] = useState(true);

  async function load() {
    try {
      setConfigs(await api.listSsoConfigs(tenantId));
    } catch (e) {
      toast.error(`Failed to load SSO configs: ${e}`);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { load(); }, []);

  async function toggleActive(c: SsoConfig) {
    try {
      await api.updateSsoConfig(c.id, { ...c, isActive: !c.isActive }, tenantId);
      load();
    } catch (e) {
      toast.error(`Update failed: ${e}`);
    }
  }

  async function del(c: SsoConfig) {
    if (!confirm(`Delete SSO config for "${c.providerName}" (${c.issuer})?`)) return;
    try {
      await api.deleteSsoConfig(c.id, tenantId);
      toast.success("Deleted");
      load();
    } catch (e) {
      toast.error(`Delete failed: ${e}`);
    }
  }

  return (
    <div className="p-6 space-y-4">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Shield className="size-5" />
          <h1 className="text-2xl font-semibold">SSO Configuration</h1>
        </div>
        <Button onClick={() => navigate("new")}><Plus className="size-4 mr-2" /> Add Provider</Button>
      </div>

      <Card>
        <CardContent className="p-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Provider</TableHead>
                <TableHead>Issuer</TableHead>
                <TableHead>Token Type</TableHead>
                <TableHead>Active</TableHead>
                <TableHead className="w-24">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {loading ? (
                <TableRow><TableCell colSpan={5} className="text-center text-muted-foreground py-8">Loading…</TableCell></TableRow>
              ) : configs.length === 0 ? (
                <TableRow><TableCell colSpan={5} className="text-center text-muted-foreground py-8">No SSO providers configured yet.</TableCell></TableRow>
              ) : configs.map(c => (
                <TableRow key={c.id}>
                  <TableCell className="font-medium capitalize">{c.providerName}</TableCell>
                  <TableCell className="font-mono text-sm truncate max-w-xs">{c.issuer}</TableCell>
                  <TableCell>
                    <Badge variant={c.tokenType === "jwt" ? "default" : "secondary"}>{c.tokenType.toUpperCase()}</Badge>
                  </TableCell>
                  <TableCell>
                    <Switch checked={c.isActive} onCheckedChange={() => toggleActive(c)} />
                  </TableCell>
                  <TableCell>
                    <div className="flex gap-1">
                      <Button size="icon" variant="ghost" onClick={() => navigate(`${c.id}/edit`)}><Pencil className="size-4" /></Button>
                      <Button size="icon" variant="ghost" className="text-destructive" onClick={() => del(c)}><Trash2 className="size-4" /></Button>
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </div>
  );
}
