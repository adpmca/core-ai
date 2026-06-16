import { ShieldAlert } from "lucide-react";
import { LocalUsersPanel } from "@/components/LocalUsersPanel";

export function PlatformAdminsPage() {
  return (
    <div className="p-6 space-y-6">
      <div className="flex items-start gap-3">
        <ShieldAlert className="size-6 text-amber-600 mt-0.5 shrink-0" />
        <div>
          <h1 className="text-xl font-semibold">Platform Administrators</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Platform admins have full access to all tenants and platform-level settings.
            At least one active platform admin must exist at all times.
          </p>
        </div>
      </div>

      <LocalUsersPanel
        tenantId={0}
        availableRoles={["master_admin"]}
        defaultRoles={["master_admin"]}
      />
    </div>
  );
}
