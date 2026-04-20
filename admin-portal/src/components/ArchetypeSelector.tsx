import { useEffect, useState } from "react";
import { Bot, Code2, Database, Search, Brain, Users, MessageSquare, Globe } from "lucide-react";
import { api, type ArchetypeSummary, type AgentArchetype } from "@/api";
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

const ICON_MAP: Record<string, React.ComponentType<{ className?: string }>> = {
  bot: Bot,
  code: Code2,
  database: Database,
  search: Search,
  brain: Brain,
  users: Users,
  chat: MessageSquare,
  globe: Globe,
};

interface ArchetypeSelectorProps {
  selected?: string;
  onSelect: (archetype: AgentArchetype) => void;
}

export function ArchetypeSelector({ selected, onSelect }: ArchetypeSelectorProps) {
  const [archetypes, setArchetypes] = useState<ArchetypeSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);

  useEffect(() => {
    api.listArchetypes()
      .then(setArchetypes)
      .catch(() => setError(true))
      .finally(() => setLoading(false));
  }, []);

  const handleSelect = async (id: string) => {
    try {
      const full = await api.getArchetype(id);
      onSelect(full);
    } catch {
      // selection failed — ignore silently
    }
  };

  if (loading) return <div className="text-sm text-muted-foreground">Loading archetypes…</div>;
  if (error) return <div className="text-sm text-muted-foreground">Could not load archetypes — the API may be unavailable.</div>;

  const grouped = archetypes.reduce<Record<string, ArchetypeSummary[]>>((acc, a) => {
    (acc[a.category] ??= []).push(a);
    return acc;
  }, {});

  const selectedArch = archetypes.find((a) => a.id === selected);
  const SelectedIcon = selectedArch ? (ICON_MAP[selectedArch.icon] ?? Bot) : null;

  return (
    <div className="space-y-2">
      <Select value={selected ?? ""} onValueChange={handleSelect}>
        <SelectTrigger>
          <SelectValue placeholder="Select an archetype…" />
        </SelectTrigger>
        <SelectContent>
          {Object.entries(grouped).map(([category, items]) => (
            <SelectGroup key={category}>
              <SelectLabel>{category}</SelectLabel>
              {items.map((arch) => (
                <SelectItem key={arch.id} value={arch.id}>
                  {arch.displayName}
                </SelectItem>
              ))}
            </SelectGroup>
          ))}
        </SelectContent>
      </Select>

      {selectedArch && (
        <div className="flex items-start gap-2 rounded-md border bg-muted/40 px-3 py-2">
          {SelectedIcon && (
            <SelectedIcon className="h-4 w-4 mt-0.5 shrink-0 text-muted-foreground" />
          )}
          <div>
            <p className="text-sm font-medium">{selectedArch.displayName}</p>
            <p className="text-xs text-muted-foreground mt-0.5">{selectedArch.description}</p>
          </div>
        </div>
      )}
    </div>
  );
}
