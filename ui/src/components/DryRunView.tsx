import { useEffect, useMemo, useState } from "react";
import {
  AlertTriangle,
  CheckCircle2,
  FileCode2,
  Loader2,
  PanelLeft,
  RefreshCw,
} from "lucide-react";
import {
  fetchValidatedJson,
  UiDryRunSchema,
  type UiDryRun,
  type UiDryRunRegistry,
} from "../schemas/apiSchemas";
import { Badge } from "./ui/badge";
import { Button } from "./ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "./ui/card";
import { Sheet, SheetContent } from "./ui/sheet";
import { SqlCodeBlock } from "./ui/sql-code-block";

interface Props {
  dryRun: UiDryRun | null;
  onLoaded: (data: UiDryRun) => void;
}

export function DryRunView({ dryRun, onLoaded }: Props) {
  const [loading, setLoading] = useState(dryRun === null);
  const [error, setError] = useState<string | null>(null);
  const [selectedRegistry, setSelectedRegistry] = useState<string | null>(null);
  const [sqlTab, setSqlTab] = useState<"main" | "deferred">("main");
  const [mobileOpen, setMobileOpen] = useState(false);

  const loadDryRun = (signal?: AbortSignal) => {
    setLoading(true);
    setError(null);
    fetchValidatedJson("/api/migrations/dry-run", UiDryRunSchema, signal)
      .then((data) => {
        onLoaded(data);
        setSelectedRegistry((prev) => {
          if (prev && data.registries.some((r) => r.registryName === prev)) return prev;
          return data.registries.find((r) => r.hasChanges)?.registryName ?? data.registries[0]?.registryName ?? null;
        });
      })
      .catch((err: unknown) => {
        if (signal?.aborted) return;
        setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!signal?.aborted) setLoading(false);
      });
  };

  useEffect(() => {
    if (dryRun !== null) {
      setSelectedRegistry((prev) => {
        if (prev && dryRun.registries.some((r) => r.registryName === prev)) return prev;
        return dryRun.registries.find((r) => r.hasChanges)?.registryName ?? dryRun.registries[0]?.registryName ?? null;
      });
      return;
    }
    const controller = new AbortController();
    loadDryRun(controller.signal);
    return () => controller.abort();
  }, [dryRun]);

  const changedRegistries = useMemo(
    () => (dryRun?.registries ?? []).filter((r) => r.hasChanges),
    [dryRun]
  );
  const syncedRegistries = useMemo(
    () => (dryRun?.registries ?? []).filter((r) => !r.hasChanges),
    [dryRun]
  );

  const activeItem: UiDryRunRegistry | null = useMemo(() => {
    if (!dryRun || !selectedRegistry) return null;
    return dryRun.registries.find((r) => r.registryName === selectedRegistry) ?? null;
  }, [dryRun, selectedRegistry]);

  useEffect(() => {
    setSqlTab("main");
  }, [selectedRegistry]);

  if (loading && !dryRun) {
    return (
      <div className="h-64 flex items-center justify-center gap-2 text-xs text-muted-foreground">
        <Loader2 className="w-4 h-4 animate-spin" />
        Comparing entities against latest snapshots...
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-4 sm:p-6 max-w-3xl mx-auto">
        <Card className="border-rose-500/40">
          <CardContent className="text-xs text-rose-600 dark:text-rose-400">{error}</CardContent>
        </Card>
      </div>
    );
  }

  if (!dryRun) return null;

  if (dryRun.changedRegistryCount === 0) {
    return (
      <div className="p-4 sm:p-6 max-w-3xl mx-auto">
        <Card>
          <CardContent className="py-10 flex flex-col items-center text-center gap-2">
            <CheckCircle2 className="w-8 h-8 text-emerald-500" />
            <div className="text-sm font-semibold">No pending schema changes</div>
            <p className="text-xs text-muted-foreground max-w-md">
              All {dryRun.registries.length} registries match your latest snapshots in{" "}
              <code className="font-mono">EntityChangelogs/</code>.
            </p>
            <Button variant="outline" size="sm" className="mt-2" disabled={loading} onClick={() => loadDryRun()}>
              <RefreshCw className={`w-3.5 h-3.5 ${loading ? "animate-spin" : ""}`} />
              Re-check
            </Button>
          </CardContent>
        </Card>
      </div>
    );
  }

  const sidebarContent = (
    <div className="flex flex-col h-full p-2 space-y-3 overflow-y-auto">
      <div className="px-2 pt-1 flex items-center justify-between">
        <span className="text-xs font-semibold text-muted-foreground">
          Pending Changes ({changedRegistries.length})
        </span>
        <Button variant="ghost" size="icon" disabled={loading} onClick={() => loadDryRun()} title="Refresh dry run">
          <RefreshCw className={`w-3.5 h-3.5 ${loading ? "animate-spin" : ""}`} />
        </Button>
      </div>

      <div className="space-y-0.5">
        {changedRegistries.map((reg) => {
          const isActive = selectedRegistry === reg.registryName;
          return (
            <button
              key={reg.registryName}
              onClick={() => {
                setSelectedRegistry(reg.registryName);
                setMobileOpen(false);
              }}
              className={`w-full text-left px-2.5 py-2 rounded-md text-xs flex items-center justify-between gap-2 transition cursor-pointer ${
                isActive
                  ? "bg-accent text-accent-foreground font-medium"
                  : "text-muted-foreground hover:bg-accent/50 hover:text-foreground"
              }`}
            >
              <span className="flex items-center gap-1.5 min-w-0">
                {reg.hasDestructiveChanges && (
                  <span className="w-2 h-2 rounded-full bg-rose-500 shrink-0" title="Contains destructive drops" />
                )}
                <span className="truncate">{reg.registryName}</span>
              </span>
              <span className="font-mono text-[11px] tabular-nums shrink-0 opacity-80">
                {reg.operations.length}
              </span>
            </button>
          );
        })}
      </div>

      {syncedRegistries.length > 0 && (
        <div className="pt-3 border-t border-border space-y-1">
          <div className="px-2 text-xs font-semibold text-muted-foreground">
            In Sync ({syncedRegistries.length})
          </div>
          {syncedRegistries.map((reg) => (
            <div
              key={reg.registryName}
              className="px-2.5 py-1.5 text-xs text-muted-foreground/70 flex items-center justify-between"
            >
              <span className="truncate">{reg.registryName}</span>
              <CheckCircle2 className="w-3.5 h-3.5 text-emerald-500/70 shrink-0" />
            </div>
          ))}
        </div>
      )}
    </div>
  );

  const displayedSql =
    activeItem && sqlTab === "deferred" && activeItem.deferredSqlScript
      ? activeItem.deferredSqlScript
      : activeItem?.sqlScript ?? "";

  return (
    <div className="flex flex-col lg:flex-row lg:h-[calc(100vh-6.75rem)] lg:overflow-hidden">
      {/* Mobile trigger bar */}
      <div className="lg:hidden px-3 py-2 border-b border-border bg-muted/30 flex items-center justify-between gap-2">
        <Button variant="outline" size="sm" onClick={() => setMobileOpen(true)}>
          <PanelLeft className="w-3.5 h-3.5" />
          <span className="truncate max-w-56">{activeItem?.registryName ?? "Select Registry"}</span>
        </Button>
        <Badge variant="warning">{dryRun.changedRegistryCount} changed</Badge>
      </div>

      <Sheet open={mobileOpen} onOpenChange={setMobileOpen}>
        <SheetContent title="Pending Registries">{sidebarContent}</SheetContent>
      </Sheet>

      <aside className="hidden lg:flex w-72 shrink-0 border-r border-border bg-muted/20 flex-col">
        {sidebarContent}
      </aside>

      <main className="flex-1 min-w-0 overflow-y-auto p-3 sm:p-6">
        <div className="max-w-5xl mx-auto space-y-5">
          {/* Summary Counters */}
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 w-full sm:w-auto">
              <CounterPill label="Registries" value={dryRun.changedRegistryCount} tone="neutral" />
              <CounterPill label="Additions" value={dryRun.addCount} tone="emerald" />
              <CounterPill label="Alterations" value={dryRun.alterCount} tone="amber" />
              <CounterPill label="Drops" value={dryRun.dropCount} tone={dryRun.dropCount > 0 ? "rose" : "neutral"} />
            </div>

            <div className="text-[11px] font-mono text-muted-foreground bg-muted/60 border border-border rounded-md px-2.5 py-1.5">
              db-migrator generate --name &lt;Name&gt;
            </div>
          </div>

          {activeItem && (
            <>
              {activeItem.hasDestructiveChanges && (
                <Card className="border-rose-500/40 bg-rose-500/5">
                  <CardContent className="py-3 flex items-center gap-2 text-xs text-rose-700 dark:text-rose-400">
                    <AlertTriangle className="w-4 h-4 shrink-0" />
                    <span>
                      <strong>{activeItem.registryName}</strong> includes {activeItem.dropCount} destructive drop
                      operation{activeItem.dropCount === 1 ? "" : "s"}. Review carefully before generating.
                    </span>
                  </CardContent>
                </Card>
              )}

              {/* Structured Pending Operations */}
              <Card className="overflow-hidden">
                <CardHeader>
                  <CardTitle>
                    {activeItem.registryName} — Pending Operations ({activeItem.operations.length})
                  </CardTitle>
                </CardHeader>
                <div className="divide-y divide-border/70">
                  {activeItem.operations.map((op, idx) => (
                    <div key={idx} className="px-4 sm:px-5 py-2.5 flex flex-wrap items-baseline gap-2 text-xs">
                      <Badge
                        variant={op.kind === "Add" ? "success" : op.kind === "Drop" ? "destructive" : "warning"}
                        className="w-14 justify-center shrink-0"
                      >
                        {op.kind.toUpperCase()}
                      </Badge>
                      <Badge variant="outline" className="shrink-0">
                        {op.objectType}
                      </Badge>
                      <span className="font-mono font-semibold text-foreground break-words">{op.target}</span>
                      <span className="font-mono text-muted-foreground break-words">{op.summary}</span>
                    </div>
                  ))}
                </div>
              </Card>

              {/* Incremental SQL */}
              {(activeItem.sqlScript || activeItem.deferredSqlScript) && (
                <Card className="overflow-hidden">
                  <CardHeader>
                    <CardTitle>
                      <FileCode2 className="w-4 h-4 text-muted-foreground" />
                      Incremental SQL Preview
                    </CardTitle>
                    {activeItem.deferredSqlScript && (
                      <div className="inline-flex rounded-md border border-border bg-muted p-0.5 text-xs">
                        <button
                          onClick={() => setSqlTab("main")}
                          className={`px-2 py-1 rounded text-[11px] font-medium cursor-pointer ${
                            sqlTab === "main" ? "bg-background text-foreground shadow-xs" : "text-muted-foreground"
                          }`}
                        >
                          Main SQL
                        </button>
                        <button
                          onClick={() => setSqlTab("deferred")}
                          className={`px-2 py-1 rounded text-[11px] font-medium cursor-pointer ${
                            sqlTab === "deferred" ? "bg-background text-foreground shadow-xs" : "text-muted-foreground"
                          }`}
                        >
                          Deferred FKs
                        </button>
                      </div>
                    )}
                  </CardHeader>
                  <SqlCodeBlock code={displayedSql} maxHeightClass="max-h-[560px]" />
                </Card>
              )}
            </>
          )}
        </div>
      </main>
    </div>
  );
}

function CounterPill({
  label,
  value,
  tone,
}: {
  label: string;
  value: number;
  tone: "neutral" | "emerald" | "amber" | "rose";
}) {
  const styles = {
    neutral: "border-border bg-muted/50 text-foreground",
    emerald: "border-emerald-500/30 bg-emerald-500/10 text-emerald-700 dark:text-emerald-400",
    amber: "border-amber-500/30 bg-amber-500/10 text-amber-700 dark:text-amber-400",
    rose: "border-rose-500/30 bg-rose-500/10 text-rose-700 dark:text-rose-400",
  }[tone];

  return (
    <div className={`px-3 py-2 rounded-lg border ${styles} min-w-24`}>
      <div className="text-lg font-bold font-mono tabular-nums">{value}</div>
      <div className="text-[10px] uppercase tracking-wider font-semibold opacity-80">{label}</div>
    </div>
  );
}
