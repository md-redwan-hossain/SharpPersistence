import { useEffect, useMemo, useState } from "react";
import {
  Columns3,
  Database,
  FileCode2,
  GitFork,
  Link2,
  ListFilter,
  Loader2,
  Monitor,
  Moon,
  Network,
  Search,
  ShieldCheck,
  Stethoscope,
  Sun,
  Table2,
} from "lucide-react";
import {
  fetchValidatedJson,
  UiOverviewSchema,
  UiSearchResponseSchema,
  type UiDependencies,
  type UiDoctor,
  type UiDryRun,
  type UiOverview,
  type UiSearchHit,
} from "./schemas/apiSchemas";
import { useTheme } from "./hooks/useTheme";
import { EntityExplorer, tableKey } from "./components/EntityExplorer";
import { ErdView } from "./components/ErdView";
import { DependenciesView } from "./components/DependenciesView";
import { MigrationsView } from "./components/MigrationsView";
import { DoctorView } from "./components/DoctorView";
import { Badge } from "./components/ui/badge";
import { Button } from "./components/ui/button";
import { Card, CardContent } from "./components/ui/card";
import {
  CommandDialog,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "./components/ui/command";
import { Tabs, TabsList, TabsTrigger } from "./components/ui/tabs";

type Tab = "entities" | "erd" | "dependencies" | "migrations" | "doctor";

export function App() {
  const { mode: themeMode, cycleTheme } = useTheme();
  const [overview, setOverview] = useState<UiOverview | null>(null);
  const [dependencies, setDependencies] = useState<UiDependencies | null>(null);
  const [dryRun, setDryRun] = useState<UiDryRun | null>(null);
  const [doctor, setDoctor] = useState<UiDoctor | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState<Tab>("entities");
  const [selectedTableKey, setSelectedTableKey] = useState<string | null>(null);
  const [erdRegistryFilter, setErdRegistryFilter] = useState<string | null>(null);

  // Command palette state + debounced server-side column/index/FK/constraint search
  const [cmdOpen, setCmdOpen] = useState(false);
  const [cmdQuery, setCmdQuery] = useState("");
  const [searchHits, setSearchHits] = useState<UiSearchHit[]>([]);
  const [searching, setSearching] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    fetchValidatedJson("/api/overview", UiOverviewSchema, controller.signal)
      .then(setOverview)
      .catch((err: unknown) => {
        if (controller.signal.aborted) return;
        setError(err instanceof Error ? err.message : String(err));
      });
    return () => controller.abort();
  }, []);

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setCmdOpen((open) => !open);
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  useEffect(() => {
    const q = cmdQuery.trim();
    if (!cmdOpen || q.length < 2) {
      setSearchHits([]);
      setSearching(false);
      return;
    }

    const controller = new AbortController();
    setSearching(true);
    const timer = window.setTimeout(() => {
      fetchValidatedJson(
        `/api/search?q=${encodeURIComponent(q)}&limit=40`,
        UiSearchResponseSchema,
        controller.signal
      )
        .then((res) => setSearchHits(res.hits))
        .catch(() => {
          if (!controller.signal.aborted) setSearchHits([]);
        })
        .finally(() => {
          if (!controller.signal.aborted) setSearching(false);
        });
    }, 150);

    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [cmdOpen, cmdQuery]);

  const groupedHits = useMemo(() => {
    const columns: UiSearchHit[] = [];
    const indexes: UiSearchHit[] = [];
    const foreignKeys: UiSearchHit[] = [];
    const constraints: UiSearchHit[] = [];

    for (const h of searchHits) {
      if (h.kind === "Column") columns.push(h);
      else if (h.kind === "Index") indexes.push(h);
      else if (h.kind === "ForeignKey") foreignKeys.push(h);
      else constraints.push(h);
    }

    return { columns, indexes, foreignKeys, constraints };
  }, [searchHits]);

  const jumpToEntity = (registryName: string, entityName: string) => {
    setSelectedTableKey(`${registryName}::${entityName}`);
    setTab("entities");
    setCmdOpen(false);
  };

  if (error) {
    return (
      <div className="min-h-screen flex items-center justify-center p-6 bg-background text-foreground">
        <Card className="max-w-md border-rose-500/40">
          <CardContent className="space-y-1 text-xs">
            <div className="font-semibold text-rose-500">Failed to load migrator overview</div>
            <p className="text-muted-foreground">{error}</p>
          </CardContent>
        </Card>
      </div>
    );
  }

  if (!overview) {
    return (
      <div className="min-h-screen flex items-center justify-center gap-2 bg-background text-muted-foreground text-xs">
        <Loader2 className="w-4 h-4 animate-spin" />
        Loading schema overview...
      </div>
    );
  }

  const ThemeIcon = themeMode === "system" ? Monitor : themeMode === "dark" ? Moon : Sun;
  const themeTitle = `Theme: ${themeMode} (click to cycle)`;

  return (
    <div className="min-h-screen bg-background text-foreground">
      {/* Top Navigation Bar */}
      <header className="min-h-14 px-3 sm:px-4 py-2 lg:py-0 border-b border-border bg-background/95 backdrop-blur flex flex-wrap items-center justify-between gap-2 sm:gap-4 sticky top-0 z-20">
        <div className="flex items-center gap-2.5 min-w-0 flex-1 sm:flex-initial">
          <div className="w-7 h-7 rounded-md bg-primary text-primary-foreground flex items-center justify-center shrink-0">
            <Database className="w-4 h-4" />
          </div>
          <div className="min-w-0">
            <div className="flex items-center gap-2">
              <span className="font-semibold text-sm truncate">{overview.projectName}</span>
              <Badge variant="info">{overview.dialect}</Badge>
            </div>
            <div className="hidden sm:block text-[11px] text-muted-foreground font-mono truncate">
              {overview.registries.length} registries · {overview.totalEntities} entities · {overview.runlistScriptCount} scripts
            </div>
          </div>
        </div>

        <div className="flex items-center gap-1.5 lg:order-3">
          <Button
            variant="outline"
            size="sm"
            onClick={() => setCmdOpen(true)}
            className="text-muted-foreground gap-2"
          >
            <Search className="w-3.5 h-3.5" />
            <span className="hidden sm:inline">Search...</span>
            <kbd className="hidden sm:inline-flex h-4 items-center rounded border border-border bg-muted px-1 font-mono text-[10px]">
              ⌘K
            </kbd>
          </Button>
          <Button variant="outline" size="icon" onClick={cycleTheme} title={themeTitle}>
            <ThemeIcon className="w-3.5 h-3.5" />
          </Button>
        </div>

        <Tabs
          value={tab}
          onValueChange={(v) => setTab(v as Tab)}
          className="w-full lg:w-auto lg:order-2 overflow-x-auto"
        >
          <TabsList className="w-full lg:w-auto justify-start">
            <TabsTrigger value="entities">
              <Table2 className="w-3.5 h-3.5" />
              Entities
            </TabsTrigger>
            <TabsTrigger value="erd">
              <GitFork className="w-3.5 h-3.5" />
              ERD
            </TabsTrigger>
            <TabsTrigger value="dependencies">
              <Network className="w-3.5 h-3.5" />
              Dependencies
            </TabsTrigger>
            <TabsTrigger value="migrations">
              <FileCode2 className="w-3.5 h-3.5" />
              Migrations
            </TabsTrigger>
            <TabsTrigger value="doctor">
              <Stethoscope className="w-3.5 h-3.5" />
              Doctor
              {doctor && doctor.errorCount > 0 && (
                <Badge variant="destructive" className="ml-0.5 px-1 py-0 text-[9px]">
                  {doctor.errorCount}
                </Badge>
              )}
              {doctor && doctor.errorCount === 0 && doctor.warningCount > 0 && (
                <Badge variant="warning" className="ml-0.5 px-1 py-0 text-[9px]">
                  {doctor.warningCount}
                </Badge>
              )}
            </TabsTrigger>
          </TabsList>
        </Tabs>
      </header>

      {/* Ctrl/Cmd + K Global Search Palette */}
      <CommandDialog
        open={cmdOpen}
        onOpenChange={(open) => {
          setCmdOpen(open);
          if (!open) setCmdQuery("");
        }}
      >
        <CommandInput
          value={cmdQuery}
          onValueChange={setCmdQuery}
          placeholder="Search entities, columns, indexes, FKs, constraints..."
        />
        <CommandList>
          <CommandEmpty>
            {searching ? (
              <span className="inline-flex items-center gap-1.5">
                <Loader2 className="w-3.5 h-3.5 animate-spin" />
                Searching schema...
              </span>
            ) : (
              "No matching entities, columns, indexes, or constraints."
            )}
          </CommandEmpty>

          {overview.registries.map((reg) => (
            <CommandGroup key={reg.name} heading={`Entities — ${reg.name}`}>
              {reg.tables.map((t) => (
                <CommandItem
                  key={tableKey(t)}
                  value={`${t.shortEntityName} ${t.qualifiedName} ${reg.name}`}
                  onSelect={() => jumpToEntity(t.registryName, t.entityName)}
                >
                  <span className="flex items-center gap-2 font-medium">
                    <Table2 className="w-3.5 h-3.5 text-muted-foreground" />
                    {t.shortEntityName}
                  </span>
                  <span className="font-mono text-[11px] text-muted-foreground">{t.qualifiedName}</span>
                </CommandItem>
              ))}
            </CommandGroup>
          ))}

          {groupedHits.columns.length > 0 && (
            <CommandGroup heading={`Columns (${groupedHits.columns.length})`}>
              {groupedHits.columns.map((h, i) => (
                <CommandItem
                  key={`col-${h.registryName}-${h.entityName}-${h.name}-${i}`}
                  value={`${cmdQuery} ${h.name} ${h.detail} ${h.qualifiedName}`}
                  onSelect={() => jumpToEntity(h.registryName, h.entityName)}
                >
                  <span className="flex items-center gap-2 min-w-0">
                    <Columns3 className="w-3.5 h-3.5 text-sky-500 shrink-0" />
                    <span className="font-mono font-medium truncate">{h.name}</span>
                    <span className="text-[11px] text-muted-foreground truncate">{h.detail}</span>
                  </span>
                  <span className="font-mono text-[11px] text-muted-foreground shrink-0">{h.shortEntityName}</span>
                </CommandItem>
              ))}
            </CommandGroup>
          )}

          {groupedHits.indexes.length > 0 && (
            <CommandGroup heading={`Indexes (${groupedHits.indexes.length})`}>
              {groupedHits.indexes.map((h, i) => (
                <CommandItem
                  key={`ix-${h.registryName}-${h.entityName}-${h.name}-${i}`}
                  value={`${cmdQuery} ${h.name} ${h.detail} ${h.qualifiedName}`}
                  onSelect={() => jumpToEntity(h.registryName, h.entityName)}
                >
                  <span className="flex items-center gap-2 min-w-0">
                    <ListFilter className="w-3.5 h-3.5 text-emerald-500 shrink-0" />
                    <span className="font-mono font-medium truncate">{h.name}</span>
                    <span className="text-[11px] text-muted-foreground truncate">{h.detail}</span>
                  </span>
                  <span className="font-mono text-[11px] text-muted-foreground shrink-0">{h.shortEntityName}</span>
                </CommandItem>
              ))}
            </CommandGroup>
          )}

          {groupedHits.foreignKeys.length > 0 && (
            <CommandGroup heading={`Foreign Keys (${groupedHits.foreignKeys.length})`}>
              {groupedHits.foreignKeys.map((h, i) => (
                <CommandItem
                  key={`fk-${h.registryName}-${h.entityName}-${h.name}-${i}`}
                  value={`${cmdQuery} ${h.name} ${h.detail} ${h.qualifiedName}`}
                  onSelect={() => jumpToEntity(h.registryName, h.entityName)}
                >
                  <span className="flex items-center gap-2 min-w-0">
                    <Link2 className="w-3.5 h-3.5 text-amber-500 shrink-0" />
                    <span className="font-mono font-medium truncate">{h.name}</span>
                    <span className="text-[11px] text-muted-foreground truncate">{h.detail}</span>
                  </span>
                  <span className="font-mono text-[11px] text-muted-foreground shrink-0">{h.shortEntityName}</span>
                </CommandItem>
              ))}
            </CommandGroup>
          )}

          {groupedHits.constraints.length > 0 && (
            <CommandGroup heading={`Constraints (${groupedHits.constraints.length})`}>
              {groupedHits.constraints.map((h, i) => (
                <CommandItem
                  key={`ck-${h.registryName}-${h.entityName}-${h.name}-${i}`}
                  value={`${cmdQuery} ${h.name} ${h.detail} ${h.qualifiedName}`}
                  onSelect={() => jumpToEntity(h.registryName, h.entityName)}
                >
                  <span className="flex items-center gap-2 min-w-0">
                    <ShieldCheck className="w-3.5 h-3.5 text-violet-500 shrink-0" />
                    <span className="font-mono font-medium truncate">{h.name}</span>
                    <span className="text-[11px] text-muted-foreground truncate">{h.detail}</span>
                  </span>
                  <span className="font-mono text-[11px] text-muted-foreground shrink-0">{h.shortEntityName}</span>
                </CommandItem>
              ))}
            </CommandGroup>
          )}
        </CommandList>
      </CommandDialog>

      {tab === "entities" && (
        <EntityExplorer
          overview={overview}
          selectedKey={selectedTableKey}
          onSelectTable={setSelectedTableKey}
        />
      )}

      {tab === "erd" && (
        <ErdView
          overview={overview}
          initialRegistry={erdRegistryFilter}
          onInspectTable={(key) => {
            setSelectedTableKey(key);
            setTab("entities");
          }}
        />
      )}

      {tab === "dependencies" && (
        <DependenciesView
          dependencies={dependencies}
          onLoaded={setDependencies}
          onOpenInErd={(regName) => {
            setErdRegistryFilter(regName);
            setTab("erd");
          }}
          onOpenInEntities={(regName) => {
            const first = overview.registries.find((r) => r.name === regName)?.tables[0];
            if (first) setSelectedTableKey(tableKey(first));
            setTab("entities");
          }}
        />
      )}

      {tab === "migrations" && (
        <MigrationsView
          runlistCount={overview.runlistScriptCount}
          dryRun={dryRun}
          onDryRunLoaded={setDryRun}
        />
      )}

      {tab === "doctor" && <DoctorView doctor={doctor} onDoctorLoaded={setDoctor} />}
    </div>
  );
}
