import { useEffect, useMemo, useState } from "react";
import {
  ChevronDown,
  ChevronRight,
  Code2,
  Database,
  HardDrive,
  Hash,
  KeyRound,
  Link2,
  ListFilter,
  Loader2,
  PanelLeft,
  Search,
  ShieldCheck,
  SlidersHorizontal,
  Table2,
} from "lucide-react";
import { API_ROUTES } from "../lib/apiRoutes";
import {
  fetchValidatedJson,
  UiTableDetailSchema,
  type UiOverview,
  type UiTableDetail,
  type UiTableSummary,
} from "../schemas/apiSchemas";
import { Badge } from "./ui/badge";
import { Button } from "./ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "./ui/card";
import { Input } from "./ui/input";
import { Sheet, SheetContent } from "./ui/sheet";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "./ui/table";

interface Props {
  overview: UiOverview;
  selectedKey: string | null;
  onSelectTable: (key: string) => void;
}

export function tableKey(t: Pick<UiTableSummary, "registryName" | "entityName">): string {
  return `${t.registryName}::${t.entityName}`;
}

function sameColumnSet(a: readonly string[], b: readonly string[]): boolean {
  if (a.length !== b.length || a.length === 0) return false;
  const setA = new Set(a.map((c) => c.toLowerCase()));
  if (setA.size !== b.length) return false;
  return b.every((c) => setA.has(c.toLowerCase()));
}

export function EntityExplorer({ overview, selectedKey, onSelectTable }: Props) {
  const [search, setSearch] = useState("");
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({});
  const [mobileOpen, setMobileOpen] = useState(false);
  const [detailCache, setDetailCache] = useState<Record<string, UiTableDetail>>({});
  const [loadingDetail, setLoadingDetail] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);

  const allSummaries = useMemo(
    () => overview.registries.flatMap((r) => r.tables),
    [overview.registries]
  );

  const filteredRegistries = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return overview.registries;
    return overview.registries
      .map((r) => ({
        ...r,
        tables: r.tables.filter(
          (t) =>
            t.shortEntityName.toLowerCase().includes(q) ||
            t.entityName.toLowerCase().includes(q) ||
            t.tableName.toLowerCase().includes(q) ||
            t.qualifiedName.toLowerCase().includes(q) ||
            r.name.toLowerCase().includes(q)
        ),
      }))
      .filter((r) => r.tables.length > 0);
  }, [overview.registries, search]);

  const activeSummary = useMemo(() => {
    if (selectedKey) {
      const found = allSummaries.find((t) => tableKey(t) === selectedKey);
      if (found) return found;
    }
    return allSummaries[0] ?? null;
  }, [allSummaries, selectedKey]);

  const activeKey = activeSummary ? tableKey(activeSummary) : null;
  const activeDetail = activeKey ? detailCache[activeKey] ?? null : null;

  const columnIndexesMap = useMemo(() => {
    const map = new Map<string, string[]>();
    if (!activeDetail) return map;
    for (const ix of activeDetail.indexes) {
      const cols = new Set<string>([
        ...ix.columns.map((c) => c.toLowerCase()),
        ...(ix.includedColumns ?? []).map((c) => c.toLowerCase()),
      ]);
      for (const col of cols) {
        const list = map.get(col) ?? [];
        list.push(ix.name);
        map.set(col, list);
      }
    }
    return map;
  }, [activeDetail]);

  const columnNullabilityMap = useMemo(() => {
    const map = new Map<string, boolean>();
    if (!activeDetail) return map;
    for (const c of activeDetail.columns) {
      map.set(c.columnName.toLowerCase(), c.isNullable);
    }
    return map;
  }, [activeDetail]);

  useEffect(() => {
    if (!activeSummary || !activeKey) return;
    if (detailCache[activeKey]) {
      setDetailError(null);
      return;
    }

    const controller = new AbortController();
    setLoadingDetail(true);
    setDetailError(null);

    const url = API_ROUTES.tableDetail(activeSummary.registryName, activeSummary.entityName);
    fetchValidatedJson(url, UiTableDetailSchema, controller.signal)
      .then((detail) => {
        setDetailCache((prev) => ({ ...prev, [activeKey]: detail }));
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return;
        setDetailError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoadingDetail(false);
      });

    return () => controller.abort();
  }, [activeSummary, activeKey, detailCache]);

  const jumpToTarget = (principalTable: string, principalSchema: string | null, principalEntity: string) => {
    const target =
      allSummaries.find(
        (t) =>
          t.tableName.toLowerCase() === principalTable.toLowerCase() &&
          (t.schema ?? "").toLowerCase() === (principalSchema ?? "").toLowerCase()
      ) ??
      allSummaries.find((t) => t.entityName.toLowerCase() === principalEntity.toLowerCase());
    if (target) {
      onSelectTable(tableKey(target));
    }
  };

  const sidebarContent = (
    <div className="flex flex-col h-full">
      <div className="p-3 border-b border-border">
        <div className="relative">
          <Search className="w-3.5 h-3.5 text-muted-foreground absolute left-2.5 top-2.5" />
          <Input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Filter entities or tables..."
            className="pl-8"
          />
        </div>
      </div>

      <div className="flex-1 overflow-y-auto p-2 space-y-1">
        {filteredRegistries.map((reg) => {
          const isCollapsed = collapsed[reg.name] ?? false;
          return (
            <div key={reg.name} className="mb-1">
              <button
                onClick={() => setCollapsed((prev) => ({ ...prev, [reg.name]: !isCollapsed }))}
                className="w-full flex items-center justify-between px-2 py-1.5 text-xs font-semibold text-muted-foreground hover:text-foreground rounded-md cursor-pointer"
              >
                <span className="flex items-center gap-1.5 min-w-0">
                  {isCollapsed ? <ChevronRight className="w-3.5 h-3.5 shrink-0" /> : <ChevronDown className="w-3.5 h-3.5 shrink-0" />}
                  <span className="truncate">{reg.name}</span>
                </span>
                <span className="px-1.5 py-0.5 text-[10px] font-mono tabular-nums rounded bg-muted text-muted-foreground">
                  {reg.tables.length}
                </span>
              </button>

              {!isCollapsed && (
                <div className="mt-0.5 space-y-0.5 pl-2">
                  {reg.tables.map((t) => {
                    const key = tableKey(t);
                    const isActive = activeKey === key;
                    return (
                      <button
                        key={key}
                        onClick={() => {
                          onSelectTable(key);
                          setMobileOpen(false);
                        }}
                        className={`w-full text-left px-2.5 py-1.5 rounded-md text-xs flex items-center justify-between gap-2 transition cursor-pointer ${
                          isActive
                            ? "bg-accent text-accent-foreground font-medium"
                            : "text-muted-foreground hover:bg-accent/50 hover:text-foreground"
                        }`}
                      >
                        <span className="flex items-center gap-2 min-w-0">
                          <Table2 className="w-3.5 h-3.5 shrink-0 opacity-70" />
                          <span className="truncate">{t.shortEntityName}</span>
                        </span>
                        <span className="flex items-center gap-1 shrink-0">
                          {t.excludeFromMigrations && <Badge variant="warning">RO</Badge>}
                          <span className="text-[11px] font-mono tabular-nums opacity-70">{t.columnCount}</span>
                        </span>
                      </button>
                    );
                  })}
                </div>
              )}
            </div>
          );
        })}

        {overview.postgresEnums.length > 0 && (
          <div className="pt-3 mt-3 border-t border-border px-2">
            <div className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground mb-1.5">
              PostgreSQL Enums ({overview.postgresEnums.length})
            </div>
            <div className="space-y-1.5">
              {overview.postgresEnums.map((e) => (
                <div key={`${e.schema}.${e.name}`} className="text-xs p-2 rounded-lg bg-card border border-border">
                  <div className="font-mono font-medium text-foreground">
                    {e.schema ? `${e.schema}.${e.name}` : e.name}
                  </div>
                  <div className="mt-1 flex flex-wrap gap-1">
                    {e.labels.map((l) => (
                      <Badge key={l} variant="secondary">
                        {l}
                      </Badge>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}

        {overview.sequences.length > 0 && (
          <div className="pt-3 mt-3 border-t border-border px-2">
            <div className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground mb-1.5">
              Sequences ({overview.sequences.length})
            </div>
            <div className="space-y-1.5">
              {overview.sequences.map((s) => (
                <div key={`${s.schema}.${s.name}`} className="text-xs p-2 rounded-lg bg-card border border-border">
                  <div className="font-mono font-medium flex items-center gap-1">
                    <Hash className="w-3 h-3 text-muted-foreground" />
                    {s.schema ? `${s.schema}.${s.name}` : s.name}
                  </div>
                  <div className="text-[11px] text-muted-foreground mt-0.5 font-mono">
                    {s.clrTypeName} {"\u00b7"} start {s.startValue} {"\u00b7"} inc {s.incrementBy}
                    {s.isCyclic ? " \u00b7 CYCLE" : ""}
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </div>
  );

  const tableStorageEntries = activeDetail?.storageParameters
    ? Object.entries(activeDetail.storageParameters)
    : [];

  return (
    <div className="flex flex-col lg:flex-row lg:h-[calc(100vh-3.5rem)] lg:overflow-hidden">
      {/* Mobile trigger bar */}
      <div className="lg:hidden px-3 py-2 border-b border-border bg-muted/30 flex items-center justify-between gap-2">
        <Button variant="outline" size="sm" onClick={() => setMobileOpen(true)}>
          <PanelLeft className="w-3.5 h-3.5" />
          <span className="truncate max-w-56">
            {activeSummary ? `${activeSummary.registryName} / ${activeSummary.shortEntityName}` : "Browse Entities"}
          </span>
        </Button>
        <span className="text-[11px] font-mono tabular-nums text-muted-foreground">
          {allSummaries.length} entities
        </span>
      </div>

      {/* Mobile Sheet Drawer */}
      <Sheet open={mobileOpen} onOpenChange={setMobileOpen}>
        <SheetContent title="Entities">{sidebarContent}</SheetContent>
      </Sheet>

      {/* Desktop Sidebar */}
      <aside className="hidden lg:flex w-72 shrink-0 border-r border-border bg-muted/20 flex-col">
        {sidebarContent}
      </aside>

      {/* Main Detail Pane */}
      <main className="flex-1 min-w-0 overflow-y-auto p-3 sm:p-6">
        {!activeSummary ? (
          <div className="h-full flex items-center justify-center text-sm text-muted-foreground">
            No entities found in the loaded registries.
          </div>
        ) : detailError ? (
          <Card className="max-w-2xl mx-auto border-rose-500/40">
            <CardContent className="text-xs text-rose-600 dark:text-rose-400">{detailError}</CardContent>
          </Card>
        ) : loadingDetail && !activeDetail ? (
          <div className="h-64 flex items-center justify-center gap-2 text-xs text-muted-foreground">
            <Loader2 className="w-4 h-4 animate-spin" />
            Loading {activeSummary.shortEntityName} metadata...
          </div>
        ) : activeDetail ? (
          <div className="max-w-6xl mx-auto space-y-5">
            {/* Entity Header Card */}
            <Card>
              <CardContent className="space-y-3">
                <div className="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-4">
                  <div className="min-w-0 flex-1 space-y-1.5">
                    <div className="flex flex-wrap items-center gap-1.5 text-xs text-muted-foreground">
                      <span className="font-medium break-words">{activeDetail.registryName}</span>
                      {activeDetail.excludeFromMigrations ? (
                        <Badge variant="warning">Excluded from Migrations</Badge>
                      ) : (
                        <Badge variant="success">Under Migration</Badge>
                      )}
                      {activeDetail.isUnlogged && <Badge variant="violet">UNLOGGED</Badge>}
                    </div>
                    <h1 className="text-xl sm:text-2xl font-bold tracking-tight break-words">
                      {activeDetail.shortEntityName}
                    </h1>

                    {/* Row 1: Code & Table Pills */}
                    <div className="pt-0.5 flex flex-wrap items-center gap-2">
                      <span
                        className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md bg-muted border border-border font-mono text-xs text-foreground max-w-full break-words"
                        title={activeDetail.entityName}
                      >
                        <Code2 className="w-3.5 h-3.5 shrink-0 text-violet-600 dark:text-violet-400" />
                        <span className="min-w-0 break-words">
                          {activeDetail.entityName.split(".").map((part, idx, arr) => (
                            <span key={idx}>
                              {part}
                              {idx < arr.length - 1 && (
                                <>
                                  .<wbr />
                                </>
                              )}
                            </span>
                          ))}
                        </span>
                      </span>

                      <span className="inline-flex flex-wrap items-center gap-1.5 px-2.5 py-1 rounded-md bg-muted border border-border text-xs text-foreground max-w-full">
                        <Database className="w-3.5 h-3.5 shrink-0 text-sky-600 dark:text-sky-400" />
                        <span className="font-mono break-all">{activeDetail.qualifiedName}</span>
                        {activeDetail.comment && (
                          <span className="text-muted-foreground break-words">({activeDetail.comment})</span>
                        )}
                      </span>
                    </div>

                    {/* Row 2: Tablespace & Storage Parameters Pills */}
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md bg-muted border border-border text-xs text-muted-foreground max-w-full">
                        <HardDrive className="w-3.5 h-3.5 shrink-0 text-amber-600 dark:text-amber-400" />
                        <span className="inline-flex items-baseline gap-1.5 leading-none">
                          <span>Tablespace:</span>
                          <span className="font-mono font-medium text-foreground">
                            {activeDetail.tablespace ?? "default"}
                          </span>
                        </span>
                      </span>

                      <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md bg-muted border border-border text-xs text-muted-foreground max-w-full">
                        <SlidersHorizontal className="w-3.5 h-3.5 shrink-0 text-emerald-600 dark:text-emerald-400" />
                        <span className="inline-flex flex-wrap items-baseline gap-1.5 leading-none">
                          <span>Storage Parameters:</span>
                          {tableStorageEntries.length > 0 ? (
                            <span className="font-mono font-medium text-foreground break-words">
                              {tableStorageEntries.map(([k, v]) => `${k}=${v}`).join(", ")}
                            </span>
                          ) : (
                            <span className="font-mono font-medium text-foreground">none</span>
                          )}
                        </span>
                      </span>
                    </div>
                  </div>

                  <div className="grid grid-cols-4 gap-2 text-center shrink-0">
                    <StatBox label="Cols" value={activeDetail.columns.length} />
                    <StatBox label="FKs" value={activeDetail.foreignKeys.length} />
                    <StatBox label="Idx" value={activeDetail.indexes.length} />
                    <StatBox
                      label="Checks"
                      value={activeDetail.checkConstraints.length + activeDetail.exclusionConstraints.length}
                    />
                  </div>
                </div>
              </CardContent>
            </Card>

            {/* Columns Card */}
            <Card className="overflow-hidden">
              <CardHeader>
                <CardTitle>
                  <Table2 className="w-4 h-4 text-muted-foreground" />
                  Columns ({activeDetail.columns.length})
                </CardTitle>
                {activeDetail.primaryKey && (
                  <span className="text-[11px] font-mono text-amber-600 dark:text-amber-400 inline-flex items-center gap-1">
                    <KeyRound className="w-3.5 h-3.5" />
                    {activeDetail.primaryKey.name} ({activeDetail.primaryKey.columns.join(", ")})
                  </span>
                )}
              </CardHeader>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>DB Column</TableHead>
                    <TableHead>CLR Property</TableHead>
                    <TableHead>DB Data Type</TableHead>
                    <TableHead>Nullability</TableHead>
                    <TableHead>Indexes</TableHead>
                    <TableHead>Default / Computed</TableHead>
                    <TableHead>Comment</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {activeDetail.columns.map((c) => {
                    const colIndexes = columnIndexesMap.get(c.columnName.toLowerCase()) ?? [];
                    return (
                      <TableRow key={c.columnName}>
                        <TableCell className="font-mono whitespace-nowrap">
                          <div className="flex items-center gap-1.5">
                            <span className="font-semibold text-foreground">{c.columnName}</span>
                            {c.isPrimaryKey && <Badge variant="warning">PK</Badge>}
                            {c.isForeignKey && <Badge variant="info">FK</Badge>}
                            {c.isIdentity && <Badge variant="success">ID</Badge>}
                            {c.isOwnedColumn && <Badge variant="violet">OWNED</Badge>}
                            {c.isRowVersion && <Badge variant="destructive">VER</Badge>}
                            {c.isConcurrencyToken && !c.isRowVersion && <Badge variant="destructive">CONC</Badge>}
                          </div>
                        </TableCell>
                        <TableCell className="font-mono text-muted-foreground whitespace-nowrap">
                          {c.propertyName}
                          <span className="ml-1.5 opacity-70">({c.clrTypeName})</span>
                        </TableCell>
                        <TableCell className="font-mono text-sky-700 dark:text-sky-400 whitespace-nowrap">
                          {c.sqlType}
                        </TableCell>
                        <TableCell className="font-mono whitespace-nowrap">
                          {c.isNullable ? (
                            <span className="text-muted-foreground">NULL</span>
                          ) : (
                            <span className="font-semibold text-emerald-700 dark:text-emerald-400">NOT NULL</span>
                          )}
                        </TableCell>
                        <TableCell className="font-mono whitespace-nowrap">
                          {colIndexes.length > 0 ? (
                            <span title={colIndexes.join("\n")}>
                              <Badge variant="secondary" className="tabular-nums">
                                {colIndexes.length}
                              </Badge>
                            </span>
                          ) : (
                            <span className="opacity-40">{"\u2014"}</span>
                          )}
                        </TableCell>
                        <TableCell className="font-mono text-muted-foreground">
                          {c.computedColumnSql ? (
                            <span>
                              <Badge variant="violet">{c.isComputedStored ? "STORED" : "VIRTUAL"}</Badge>{" "}
                              {c.computedColumnSql}
                            </span>
                          ) : c.defaultValueSql ? (
                            c.defaultValueSql
                          ) : (
                            <span className="opacity-40">{"\u2014"}</span>
                          )}
                        </TableCell>
                        <TableCell className="text-muted-foreground max-w-xs">
                          {c.comment ?? <span className="opacity-40">{"\u2014"}</span>}
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </Card>

            {/* Foreign Keys */}
            {activeDetail.foreignKeys.length > 0 && (
              <Card className="overflow-hidden">
                <CardHeader>
                  <CardTitle>
                    <Link2 className="w-4 h-4 text-muted-foreground" />
                    Foreign Keys ({activeDetail.foreignKeys.length})
                  </CardTitle>
                </CardHeader>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Constraint Name</TableHead>
                      <TableHead>Dependent Columns</TableHead>
                      <TableHead>References</TableHead>
                      <TableHead>Relation</TableHead>
                      <TableHead>On Delete</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {activeDetail.foreignKeys.map((fk) => {
                      const isRequired =
                        fk.dependentColumns.length > 0 &&
                        fk.dependentColumns.every((col) => columnNullabilityMap.get(col.toLowerCase()) === false);

                      const matchesPk =
                        activeDetail.primaryKey !== null &&
                        sameColumnSet(activeDetail.primaryKey.columns, fk.dependentColumns);
                      const matchesUniqueIx = activeDetail.indexes.some(
                        (ix) => ix.isUnique && !ix.filterSql && sameColumnSet(ix.columns, fk.dependentColumns)
                      );
                      const isOneToOne = matchesPk || matchesUniqueIx;

                      return (
                        <TableRow key={fk.name}>
                          <TableCell className="font-mono font-semibold whitespace-nowrap">
                            <div className="flex items-center gap-1.5">
                              <span>{fk.name}</span>
                              {fk.isCircular && <Badge variant="warning">CIRCULAR</Badge>}
                            </div>
                          </TableCell>
                          <TableCell className="font-mono whitespace-nowrap">
                            <div className="text-foreground">{fk.dependentColumns.join(", ")}</div>
                            {fk.dependentProperties.length > 0 && (
                              <div className="text-[11px] text-muted-foreground">
                                {fk.dependentProperties.join(", ")}
                              </div>
                            )}
                          </TableCell>
                          <TableCell className="font-mono whitespace-nowrap">
                            <button
                              onClick={() => jumpToTarget(fk.principalTable, fk.principalSchema, fk.principalEntityName)}
                              className="text-sky-600 dark:text-sky-400 hover:underline font-semibold cursor-pointer text-left"
                            >
                              {fk.principalQualifiedName}({fk.principalColumns.join(", ")})
                            </button>
                            {fk.principalProperties.length > 0 && (
                              <div className="text-[11px] text-muted-foreground">
                                {(fk.principalEntityName.split(/[.+]/).pop() ?? fk.principalEntityName)}
                                ({fk.principalProperties.join(", ")})
                              </div>
                            )}
                          </TableCell>
                          <TableCell className="whitespace-nowrap">
                            <div className="flex items-center gap-1.5">
                              <Badge variant={isOneToOne ? "violet" : "info"}>
                                {isOneToOne ? "1:1 One-to-One" : "N:1 Many-to-One"}
                              </Badge>
                              <Badge variant={isRequired ? "success" : "secondary"}>
                                {isRequired ? "Required" : "Optional"}
                              </Badge>
                            </div>
                          </TableCell>
                          <TableCell className="whitespace-nowrap">
                            <Badge variant="outline">{fk.onDelete.toUpperCase()}</Badge>
                          </TableCell>
                        </TableRow>
                      );
                    })}
                  </TableBody>
                </Table>
              </Card>
            )}

            {/* Indexes */}
            {activeDetail.indexes.length > 0 && (
              <Card className="overflow-hidden">
                <CardHeader>
                  <CardTitle>
                    <ListFilter className="w-4 h-4 text-muted-foreground" />
                    Indexes ({activeDetail.indexes.length})
                  </CardTitle>
                </CardHeader>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Index Name</TableHead>
                      <TableHead>Key Columns (Order &amp; Operator)</TableHead>
                      <TableHead>Included Columns</TableHead>
                      <TableHead>Filter (WHERE)</TableHead>
                      <TableHead>Storage &amp; Tablespace</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {activeDetail.indexes.map((ix) => {
                      const storageEntries = ix.storageParameters ? Object.entries(ix.storageParameters) : [];
                      return (
                        <TableRow key={ix.name}>
                          <TableCell className="font-mono whitespace-nowrap">
                            <div className="flex items-center gap-1.5">
                              <span className="font-semibold text-foreground">{ix.name}</span>
                              {ix.isUnique && <Badge variant="success">UNIQUE</Badge>}
                              <Badge variant="violet">{(ix.method ?? "btree").toUpperCase()}</Badge>
                            </div>
                          </TableCell>
                          <TableCell className="font-mono">
                            <div className="flex flex-wrap gap-1.5">
                              {ix.columns.map((col, idx) => {
                                const dir = ix.isDescending?.[idx] ? "DESC" : "ASC";
                                const op = ix.operatorsByColumn?.[col];
                                return (
                                  <span
                                    key={`${col}-${idx}`}
                                    className="inline-flex items-center gap-1 px-2 py-0.5 rounded bg-muted border border-border text-[11px]"
                                  >
                                    <span className="font-medium text-foreground">{col}</span>
                                    <span className={dir === "DESC" ? "text-amber-600 dark:text-amber-400 font-semibold" : "text-muted-foreground"}>
                                      {dir}
                                    </span>
                                    {op && (
                                      <Badge variant="info" className="px-1 py-0 text-[10px]">
                                        {op}
                                      </Badge>
                                    )}
                                  </span>
                                );
                              })}
                            </div>
                          </TableCell>
                          <TableCell className="font-mono text-muted-foreground">
                            {ix.includedColumns && ix.includedColumns.length > 0 ? (
                              <div className="flex flex-wrap gap-1">
                                {ix.includedColumns.map((col, idx) => {
                                  const prop = ix.includedProperties?.[idx];
                                  return (
                                    <Badge key={`${col}-${idx}`} variant="secondary" className="font-mono">
                                      {col}
                                      {prop && prop !== col ? ` (${prop})` : ""}
                                    </Badge>
                                  );
                                })}
                              </div>
                            ) : (
                              <span className="opacity-40">{"\u2014"}</span>
                            )}
                          </TableCell>
                          <TableCell className="font-mono text-amber-600 dark:text-amber-400 max-w-xs break-words">
                            {ix.filterSql ?? <span className="text-muted-foreground opacity-40">{"\u2014"}</span>}
                          </TableCell>
                          <TableCell className="font-mono">
                            {ix.tablespace || storageEntries.length > 0 ? (
                              <div className="flex flex-wrap gap-1">
                                {ix.tablespace && (
                                  <Badge variant="outline">TABLESPACE {ix.tablespace}</Badge>
                                )}
                                {storageEntries.map(([k, v]) => (
                                  <Badge key={k} variant="outline">
                                    {k}={v}
                                  </Badge>
                                ))}
                              </div>
                            ) : (
                              <span className="opacity-40">{"\u2014"}</span>
                            )}
                          </TableCell>
                        </TableRow>
                      );
                    })}
                  </TableBody>
                </Table>
              </Card>
            )}

            {/* Check & Exclusion Constraints */}
            {(activeDetail.checkConstraints.length > 0 || activeDetail.exclusionConstraints.length > 0) && (
              <Card className="overflow-hidden">
                <CardHeader>
                  <CardTitle>
                    <ShieldCheck className="w-4 h-4 text-muted-foreground" />
                    Constraints ({activeDetail.checkConstraints.length + activeDetail.exclusionConstraints.length})
                  </CardTitle>
                </CardHeader>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Type</TableHead>
                      <TableHead>Constraint Name</TableHead>
                      <TableHead>Expression / Definition</TableHead>
                      <TableHead>Filter (WHERE)</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {activeDetail.checkConstraints.map((ck) => (
                      <TableRow key={ck.name}>
                        <TableCell className="whitespace-nowrap">
                          <Badge variant="success">CHECK</Badge>
                        </TableCell>
                        <TableCell className="font-mono font-semibold whitespace-nowrap">
                          {ck.name}
                        </TableCell>
                        <TableCell className="font-mono text-muted-foreground break-words">
                          {ck.sql}
                        </TableCell>
                        <TableCell className="font-mono text-muted-foreground">
                          <span className="opacity-40">{"\u2014"}</span>
                        </TableCell>
                      </TableRow>
                    ))}
                    {activeDetail.exclusionConstraints.map((ex) => (
                      <TableRow key={ex.name}>
                        <TableCell className="whitespace-nowrap">
                          <Badge variant="violet">EXCLUDE ({ex.method.toUpperCase()})</Badge>
                        </TableCell>
                        <TableCell className="font-mono font-semibold whitespace-nowrap">
                          {ex.name}
                        </TableCell>
                        <TableCell className="font-mono text-muted-foreground break-words">
                          {ex.elements.join(", ")}
                        </TableCell>
                        <TableCell className="font-mono text-amber-600 dark:text-amber-400 break-words">
                          {ex.filterSql ?? <span className="text-muted-foreground opacity-40">{"\u2014"}</span>}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </Card>
            )}
          </div>
        ) : null}
      </main>
    </div>
  );
}

function StatBox({ label, value }: { label: string; value: number }) {
  return (
    <div className="px-2.5 py-1.5 rounded-lg bg-muted/60 border border-border min-w-14">
      <div className="text-sm font-bold font-mono tabular-nums">{value}</div>
      <div className="text-[10px] uppercase tracking-wider text-muted-foreground">{label}</div>
    </div>
  );
}
