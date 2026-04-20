/**
 * SsoConfigEditor — full-page form for creating and editing SSO provider configs.
 *
 * Routes:
 *   /settings/sso/new       — create a new provider
 *   /settings/sso/:id/edit  — edit an existing provider
 */
import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { api, type CreateSsoConfigDto } from "@/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { ArrowLeft, Save, Info, ChevronDown, ChevronUp } from "lucide-react";
import { toast } from "sonner";

const CLAIM_FIELDS = [
  { key: "TenantId",    default: "tenant_id",       desc: "Integer tenant identifier" },
  { key: "TenantName",  default: "tenant_name",      desc: "Human-readable tenant name" },
  { key: "UserId",      default: "sub",              desc: "Unique user identifier (OIDC subject)" },
  { key: "Email",       default: "email",            desc: 'User email — try "mail" for Exchange/O365' },
  { key: "DisplayName", default: "name",             desc: 'User display name — try "displayName" for Azure AD' },
  { key: "SiteIds",     default: "site_ids",         desc: "Array or comma-list of integer site IDs the user can access" },
  { key: "Roles",       default: "roles",            desc: "Array or comma-list of role strings" },
  { key: "AgentAccess", default: "agent_access",     desc: "Array or comma-list of agent IDs this user may invoke" },
  { key: "TeamApiKey",  default: "litellm_team_key", desc: "LiteLLM team API key forwarded with LLM calls" },
] as const;

const emptyForm: CreateSsoConfigDto = {
  providerName: "generic",
  issuer: "",
  clientId: "",
  clientSecret: "",
  tokenType: "jwt",
  authority: "",
  authorizationEndpoint: "",
  tokenEndpoint: "",
  userinfoEndpoint: "",
  introspectionEndpoint: "",
  audience: "",
  proxyBaseUrl: "",
  proxyAdminEmail: "",
  useRoleMappings: false,
  useTeamMappings: false,
  claimMappingsJson: "",
  logoutUrl: "",
  emailDomains: "",
};

export function SsoConfigEditor({ tenantId = 1 }: { tenantId?: number }) {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const isEdit = Boolean(id);

  const [form, setForm] = useState<CreateSsoConfigDto>(emptyForm);
  const [loading, setLoading] = useState(isEdit);
  const [saving, setSaving] = useState(false);
  const [claimHelpOpen, setClaimHelpOpen] = useState(false);

  const set = (field: keyof CreateSsoConfigDto, value: unknown) =>
    setForm(f => ({ ...f, [field]: value }));

  useEffect(() => {
    if (!isEdit || !id) return;
    api.getSsoConfig(Number(id), tenantId)
      .then(c => setForm({
        providerName: c.providerName,
        issuer: c.issuer,
        clientId: c.clientId,
        clientSecret: "",
        tokenType: c.tokenType,
        authority: c.authority ?? "",
        authorizationEndpoint: c.authorizationEndpoint ?? "",
        tokenEndpoint: c.tokenEndpoint ?? "",
        userinfoEndpoint: c.userinfoEndpoint ?? "",
        introspectionEndpoint: c.introspectionEndpoint ?? "",
        audience: c.audience,
        proxyBaseUrl: c.proxyBaseUrl,
        proxyAdminEmail: c.proxyAdminEmail ?? "",
        useRoleMappings: c.useRoleMappings,
        useTeamMappings: c.useTeamMappings,
        claimMappingsJson: c.claimMappingsJson ?? "",
        logoutUrl: c.logoutUrl ?? "",
        emailDomains: c.emailDomains ?? "",
      }))
      .catch(() => toast.error("Failed to load SSO config"))
      .finally(() => setLoading(false));
  }, [id, isEdit, tenantId]);

  async function save() {
    setSaving(true);
    try {
      if (isEdit && id) {
        await api.updateSsoConfig(Number(id), { ...form, isActive: true }, tenantId);
        toast.success("SSO config updated");
      } else {
        await api.createSsoConfig(form, tenantId);
        toast.success("SSO config created");
      }
      navigate("/settings/sso");
    } catch (e) {
      toast.error(`Save failed: ${e}`);
    } finally {
      setSaving(false);
    }
  }

  if (loading) {
    return (
      <div className="p-8 text-sm text-muted-foreground">Loading…</div>
    );
  }

  return (
    <div className="p-6 max-w-4xl mx-auto space-y-6">
      {/* Header */}
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="sm" onClick={() => navigate("/settings/sso")} className="gap-1 -ml-1">
          <ArrowLeft className="size-4" /> Back
        </Button>
        <div>
          <h1 className="text-2xl font-semibold">{isEdit ? "Edit SSO Provider" : "Add SSO Provider"}</h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            Configure an external identity provider for tenant SSO login.
          </p>
        </div>
      </div>

      {/* Basic settings */}
      <Card>
        <CardHeader>
          <CardTitle>Provider</CardTitle>
          <CardDescription>Identity provider type and core credentials.</CardDescription>
        </CardHeader>
        <CardContent className="grid grid-cols-2 gap-6">
          <div className="space-y-1.5">
            <Label>Provider</Label>
            <Select value={form.providerName} onValueChange={v => set("providerName", v)}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="google">Google</SelectItem>
                <SelectItem value="azure">Azure AD</SelectItem>
                <SelectItem value="okta">Okta</SelectItem>
                <SelectItem value="generic">Generic OIDC</SelectItem>
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-1.5">
            <Label>Token Type</Label>
            <Select value={form.tokenType} onValueChange={v => set("tokenType", v)}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="jwt">JWT</SelectItem>
                <SelectItem value="opaque">Opaque</SelectItem>
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-1.5 col-span-2">
            <Label>Issuer <span className="text-muted-foreground text-xs">(JWT iss claim)</span></Label>
            <Input value={form.issuer} onChange={e => set("issuer", e.target.value)} placeholder="https://accounts.google.com" />
          </div>

          <div className="space-y-1.5">
            <Label>Client ID</Label>
            <Input value={form.clientId} onChange={e => set("clientId", e.target.value)} />
          </div>

          <div className="space-y-1.5">
            <Label>
              Client Secret
              {isEdit && <span className="text-muted-foreground text-xs ml-1">(leave blank to keep existing)</span>}
            </Label>
            <Input type="password" value={form.clientSecret} onChange={e => set("clientSecret", e.target.value)} />
          </div>

          <div className="space-y-1.5 col-span-2">
            <Label>Audience</Label>
            <Input value={form.audience} onChange={e => set("audience", e.target.value)} />
          </div>
        </CardContent>
      </Card>

      {/* Endpoints */}
      <Card>
        <CardHeader>
          <CardTitle>Endpoints</CardTitle>
          <CardDescription>
            Set the Authority / Discovery URL and the remaining endpoints are optional overrides.
          </CardDescription>
        </CardHeader>
        <CardContent className="grid grid-cols-2 gap-6">
          <div className="space-y-1.5 col-span-2">
            <Label>Authority / Discovery URL <span className="text-muted-foreground text-xs">(auto-populates endpoints)</span></Label>
            <Input value={form.authority} onChange={e => set("authority", e.target.value)} placeholder="https://login.microsoftonline.com/{tenant}" />
          </div>

          <div className="space-y-1.5">
            <Label>Authorization Endpoint</Label>
            <Input value={form.authorizationEndpoint} onChange={e => set("authorizationEndpoint", e.target.value)} />
          </div>

          <div className="space-y-1.5">
            <Label>Token Endpoint</Label>
            <Input value={form.tokenEndpoint} onChange={e => set("tokenEndpoint", e.target.value)} />
          </div>

          <div className="space-y-1.5">
            <Label>
              Userinfo Endpoint
              {form.tokenType === "opaque" && (
                <span className="text-muted-foreground text-xs ml-1">— also validates opaque tokens when no introspection endpoint is set</span>
              )}
            </Label>
            <Input value={form.userinfoEndpoint} onChange={e => set("userinfoEndpoint", e.target.value)} />
          </div>

          {form.tokenType === "opaque" && (
            <div className="space-y-1.5">
              <Label>Introspection Endpoint <span className="text-muted-foreground text-xs">(optional — falls back to Userinfo)</span></Label>
              <Input value={form.introspectionEndpoint} onChange={e => set("introspectionEndpoint", e.target.value)} />
            </div>
          )}

          <div className="space-y-1.5">
            <Label>Logout URL <span className="text-muted-foreground text-xs">(redirect after portal sign-out)</span></Label>
            <Input value={form.logoutUrl} onChange={e => set("logoutUrl", e.target.value)} placeholder="https://your-idp.example.com/auth/logout" />
          </div>
        </CardContent>
      </Card>

      {/* Proxy */}
      <Card>
        <CardHeader>
          <CardTitle>Proxy</CardTitle>
          <CardDescription>Optional proxy settings for environments that route IdP traffic through a gateway.</CardDescription>
        </CardHeader>
        <CardContent className="grid grid-cols-2 gap-6">
          <div className="space-y-1.5">
            <Label>Proxy Base URL</Label>
            <Input value={form.proxyBaseUrl} onChange={e => set("proxyBaseUrl", e.target.value)} />
          </div>
          <div className="space-y-1.5">
            <Label>Proxy Admin Email</Label>
            <Input type="email" value={form.proxyAdminEmail} onChange={e => set("proxyAdminEmail", e.target.value)} />
          </div>
        </CardContent>
      </Card>

      {/* Mappings */}
      <Card>
        <CardHeader>
          <CardTitle>Mappings</CardTitle>
          <CardDescription>Control how provider roles, groups, and claim names translate to Diva identity fields.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-6">
          <div className="flex gap-8">
            <div className="flex items-center gap-2">
              <Switch checked={form.useRoleMappings} onCheckedChange={v => set("useRoleMappings", v)} />
              <Label>Map provider roles → app roles</Label>
            </div>
            <div className="flex items-center gap-2">
              <Switch checked={form.useTeamMappings} onCheckedChange={v => set("useTeamMappings", v)} />
              <Label>Map provider groups → teams</Label>
            </div>
          </div>

          <div className="space-y-1.5">
            <div className="flex items-center justify-between">
              <Label>
                Claim Mappings JSON
                <span className="text-muted-foreground text-xs ml-1">(optional — overrides default claim names)</span>
              </Label>
              <button
                type="button"
                onClick={() => setClaimHelpOpen(v => !v)}
                className="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground"
              >
                <Info className="size-3" />
                Available fields
                {claimHelpOpen ? <ChevronUp className="size-3" /> : <ChevronDown className="size-3" />}
              </button>
            </div>
            <Textarea
              value={form.claimMappingsJson}
              onChange={e => set("claimMappingsJson", e.target.value)}
              placeholder='{"TenantId":"tid","UserId":"sub","Roles":"groups"}'
              className="font-mono text-sm"
              rows={3}
            />
            {claimHelpOpen && (
              <div className="rounded-md border bg-muted/40 p-4 space-y-3 text-sm">
                <p className="text-muted-foreground text-xs">
                  Each key maps a Diva identity field to the claim name your provider emits.
                  Only include fields that differ from the defaults. Values support JSON arrays and comma-separated strings.
                </p>
                <table className="w-full text-xs">
                  <thead>
                    <tr className="border-b text-left text-muted-foreground">
                      <th className="pb-1.5 pr-4 font-medium w-36">Field</th>
                      <th className="pb-1.5 pr-4 font-medium w-40">Default claim</th>
                      <th className="pb-1.5 font-medium">Description</th>
                    </tr>
                  </thead>
                  <tbody className="font-mono">
                    {CLAIM_FIELDS.map(f => (
                      <tr key={f.key} className="border-b border-border/40 last:border-0">
                        <td className="py-1.5 pr-4 text-foreground">{f.key}</td>
                        <td className="py-1.5 pr-4 text-primary">{f.default}</td>
                        <td className="py-1.5 font-sans text-muted-foreground">{f.desc}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                <p className="text-muted-foreground text-xs pt-1">
                  Example: <code className="bg-muted rounded px-1">{"{"}"TenantId":"tid","Roles":"groups","Email":"mail"{"}"}</code>
                </p>
              </div>
            )}
          </div>

          <div className="space-y-1.5">
            <Label>Email Domains <span className="text-muted-foreground text-xs">(comma-separated — restricts which addresses can sign in)</span></Label>
            <Input
              value={form.emailDomains}
              onChange={e => set("emailDomains", e.target.value)}
              placeholder="example.com, contoso.com"
            />
          </div>
        </CardContent>
      </Card>

      {/* Actions */}
      <div className="flex justify-end gap-3 pb-8">
        <Button variant="outline" onClick={() => navigate("/settings/sso")}>Cancel</Button>
        <Button onClick={save} disabled={saving} className="gap-2">
          <Save className="size-4" />
          {saving ? "Saving…" : isEdit ? "Update Provider" : "Create Provider"}
        </Button>
      </div>
    </div>
  );
}
