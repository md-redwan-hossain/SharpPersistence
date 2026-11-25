import { useEffect, useMemo, useState } from "react";
import { AlertTriangle, FileCode2, Loader2, Search } from "lucide-react";
import { API_ROUTES } from "../lib/apiRoutes";
import {
  fetchValidatedJson,
  UiRunlistSchema,
  type UiRunlist,
  type UiRunlistScript,
} from "../schemas/apiSchemas";
import { Badge } from "./ui/badge";
import { Card, CardContent } from "./ui/card";
import { Input } from "./ui/input";
import { SqlCodeBlock } from "./ui/sql-code-block";

export function RunlistView() {
  const [runlist, setRunlist] = useState<UiRunlist | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [sqlCache, setSqlCache] = useState<Record<string, string>>({});
  const [loadingSql, setLoadingSql] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    fetchValidatedJson(API_ROUTES.runlist, UiRunlistSchema, controller.signal)
      .then((data) => {
        setRunlist(data);
        setSelectedId((prev) => prev ?? data.scripts[0]?.id ?? null);
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return;
        setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });

    return () => controller.abort();
  }, []);

  const filtered = useMemo(() => {
    if (!runlist) return [];
    const q = search.trim().toLowerCase();
    if (!q) return runlist.scripts;
    return runlist.scripts.filter(
      (s) =>
        s.scriptName.toLowerCase().includes(q) ||
        s.registryName.toLowerCase().includes(q) ||
        s.id.toLowerCase().includes(q)
    );
  }, [runlist, search]);

  const activeScript: UiRunlistScript | null = useMemo(() => {
    if (!runlist) return null;
    if (selectedId) {
      const match = runlist.scripts.find((s) => s.id === selectedId);
      if (match) return match;
    }
    return runlist.scripts[0] ?? null;
  }, [runlist, selectedId]);

  useEffect(() => {
    if (!activeScript || sqlCache[activeScript.id] !== undefined) return;

    const controller = new AbortController();
    setLoadingSql(true);
    fetch(API_ROUTES.runlistSql(activeScript.id), { signal: controller.signal })
      .then((res) => (res.ok ? res.text() : "-- Failed to load SQL script"))
      .then((text) => setSqlCache((prev) => ({ ...prev, [activeScript.id]: text })))
      .catch(() => {
        // Aborted or failed
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoadingSql(false);
      });

    return () => controller.abort();
  }, [activeScript, sqlCache]);

  const activeSql = activeScript ? sqlCache[activeScript.id] ?? "" : "";

  if (loading) {
    return (
      <div className="h-64 flex items-center justify-center gap-2 text-xs text-muted-foreground">
        <Loader2 className="w-4 h-4 animate-spin" />
        Loading runlist...
      </div>
    );
  }

  if (error || runlist?.error) {
    return (
      <div className="p-4 sm:p-6 max-w-3xl mx-auto">
        <Card className="border-amber-500/40 bg-amber-500/5">
          <CardContent className="flex items-start gap-3">
            <AlertTriangle className="w-5 h-5 text-amber-500 shrink-0 mt-0.5" />
            <div className="text-xs space-y-1">
              <div className="font-semibold text-amber-600 dark:text-amber-400">Runlist Error</div>
              <p className="text-muted-foreground">{error ?? runlist?.error}</p>
            </div>
          </CardContent>
        </Card>
      </div>
    );
  }

  return (
    <div className="flex flex-col lg:flex-row lg:h-[calc(100vh-6.75rem)] lg:overflow-hidden">
      {/* Left Script List */}
      <aside className="w-full lg:w-96 shrink-0 border-b lg:border-b-0 lg:border-r border-border bg-muted/20 flex flex-col max-h-72 lg:max-h-none">
        <div className="p-3 border-b border-border space-y-2">
          <div className="flex items-center justify-between text-xs">
            <span className="font-semibold">Execution Order</span>
            <span className="text-[11px] font-mono text-muted-foreground tabular-nums">
              {runlist?.scripts.length ?? 0} scripts
            </span>
          </div>
          <div className="relative">
            <Search className="w-3.5 h-3.5 text-muted-foreground absolute left-2.5 top-2.5" />
            <Input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Filter by script, registry, or UUID..."
              className="pl-8"
            />
          </div>
        </div>

        <div className="flex-1 overflow-y-auto divide-y divide-border/70">
          {filtered.length === 0 ? (
            <div className="p-6 text-center text-xs text-muted-foreground">No migration scripts found.</div>
          ) : (
            filtered.map((s) => {
              const isActive = activeScript?.id === s.id;
              return (
                <button
                  key={s.id}
                  onClick={() => setSelectedId(s.id)}
                  className={`w-full text-left px-4 py-3 transition cursor-pointer ${
                    isActive
                      ? "bg-accent border-l-2 border-l-primary"
                      : "hover:bg-accent/50"
                  }`}
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-xs font-mono tabular-nums text-muted-foreground">#{s.order}</span>
                    <div className="flex items-center gap-1.5">
                      <Badge variant="secondary">{s.registryName}</Badge>
                      {s.isOverridden && <Badge variant="warning">override</Badge>}
                    </div>
                  </div>
                  <div className="mt-1 text-xs font-mono font-medium truncate text-foreground">
                    {s.scriptName}
                  </div>
                  <div className="mt-0.5 text-[11px] font-mono text-muted-foreground truncate">{s.id}</div>
                </button>
              );
            })
          )}
        </div>
      </aside>

      {/* Right SQL Viewer */}
      <main className="flex-1 min-w-0 flex flex-col overflow-hidden bg-background">
        {!activeScript ? (
          <div className="flex-1 flex items-center justify-center text-xs text-muted-foreground p-6">
            Select a migration script to inspect its SQL.
          </div>
        ) : (
          <>
            <div className="px-4 sm:px-5 py-3 border-b border-border bg-card flex flex-wrap items-center justify-between gap-3">
              <div className="min-w-0">
                <div className="flex items-center gap-2 text-xs font-mono font-semibold truncate">
                  <FileCode2 className="w-4 h-4 text-sky-600 dark:text-sky-400 shrink-0" />
                  <span className="truncate">
                    {activeScript.registryName}/{activeScript.scriptName}
                  </span>
                </div>
                <div className="text-[11px] font-mono text-muted-foreground mt-0.5">
                  #{activeScript.order} · {activeScript.id}
                </div>
              </div>
            </div>
            <div className="flex-1 overflow-auto">
              {loadingSql ? (
                <div className="p-4 flex items-center gap-2 text-xs text-muted-foreground">
                  <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  Loading SQL...
                </div>
              ) : (
                <SqlCodeBlock code={activeSql} />
              )}
            </div>
          </>
        )}
      </main>
    </div>
  );
}
