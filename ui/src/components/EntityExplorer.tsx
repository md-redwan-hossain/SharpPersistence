import { useMemo, useState } from "react";
import {
  ChevronDown,
  ChevronRight,
  Copy,
  Check,
  Database,
  KeyRound,
  Link2,
  Search,
  ShieldCheck,
  Table2,
  ListFilter,
  Hash,
} from "lucide-react";
import type { UiModel, UiTable } from "../types";

interface Props {
  model: UiModel;
  selectedKey: string | null;
  onSelectTable: (key: string) => void;
}

export function tableKey(t: Pick<UiTable, "registryName" | "entityName">): string {
  return `${t.registryName}::${t.entityName}`;
}

export function EntityExplorer({ model, selectedKey, onSelectTable }: Props) {
  const [search, setSearch] = useState("");
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({});
  const [copiedDdl, setCopiedDdl] = useState(false);

  const allTables = useMemo(
    () => model.registries.flatMap((r) => r.tables),
    [model.registries]
  );

  const filteredRegistries = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return model.registries;
    return model.registries
      .map((r) => ({
        ...r,
        tables: r.tables.filter(
          (t) =>
            t.entityName.toLowerCase().includes(q) ||
            t.tableName.toLowerCase().includes(q) ||
            t.qualifiedName.toLowerCase().includes(q) ||
            r.name.toLowerCase().includes(q) ||
            t.columns.some(
              (c) =>
                c.columnName.toLowerCase().includes(q) ||
                c.propertyName.toLowerCase().includes(q)
            )
        ),
      }))
      .filter((r) => r.tables.length > 0);
  }, [model.registries, search]);

  const activeTable = useMemo(() => {
    if (selectedKey) {
      const found = allTables.find((t) => tableKey(t) === selectedKey);
      if (found) return found;
    }
    return allTables[0] ?? null;
  }, [allTables, selectedKey]);

  const jumpToTarget = (principalTable: string, principalSchema: string | null, principalEntity: string) => {
    const target =
      allTables.find(
        (t) =>
          t.tableName.toLowerCase() === principalTable.toLowerCase() &&
          (t.schema ?? "").toLowerCase() === (principalSchema ?? "").toLowerCase()
      ) ??
      allTables.find((t) => t.entityName.toLowerCase() === principalEntity.toLowerCase());
    if (target) {
      onSelectTable(tableKey(target));
    }
  };

  const handleCopyDdl = async (ddl: string) => {
    try {
      await navigator.clipboard.writeText(ddl);
      setCopiedDdl(true);
      setTimeout(() => setCopiedDdl(false), 1600);
    } catch {
      // Clipboard API unavailable
    }
  };

  return (
    <div className="flex h-[calc(100vh-3.5rem)] overflow-hidden">
      {/* Left Sidebar: 2-level Registry -> Entity */}
      <aside className="w-76 shrink-0 border-r border-slate-200 dark:border-slate-800 bg-slate-50/70 dark:bg-slate-900/50 flex flex-col">
        <div className="p-3 border-b border-slate-200 dark:border-slate-800">
          <div className="relative">
            <Search className="w-4 h-4 text-slate-400 absolute left-2.5 top-2.5" />
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search entities, tables, columns..."
              className="w-full pl-8 pr-3 py-1.5 text-sm rounded-lg bg-white dark:bg-slate-950 border border-slate-200 dark:border-slate-800 focus:outline-none focus:ring-2 focus:ring-sky-500/40"
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
                  className="w-full flex items-center justify-between px-2 py-1.5 text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-100 rounded-md"
                >
                  <span className="flex items-center gap-1.5 truncate">
                    {isCollapsed ? <ChevronRight className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />}
                    <span className="truncate">{reg.name}</span>
                  </span>
                  <span className="px-1.5 py-0.5 text-[10px] rounded bg-slate-200/80 dark:bg-slate-800 text-slate-600 dark:text-slate-300">
                    {reg.tables.length}
                  </span>
                </button>

                {!isCollapsed && (
                  <div className="mt-0.5 space-y-0.5 pl-2">
                    {reg.tables.map((t) => {
                      const key = tableKey(t);
                      const isActive = activeTable && tableKey(activeTable) === key;
                      return (
                        <button
                          key={key}
                          onClick={() => onSelectTable(key)}
                          className={`w-full text-left px-2.5 py-1.5 rounded-lg text-sm flex items-center justify-between gap-2 transition ${
                            isActive
                              ? "bg-sky-500/15 text-sky-600 dark:text-sky-400 font-medium border border-sky-500/30"
                              : "text-slate-700 dark:text-slate-300 hover:bg-slate-200/60 dark:hover:bg-slate-800/60 border border-transparent"
                          }`}
                        >
                          <span className="flex items-center gap-2 min-w-0">
                            <Table2 className="w-3.5 h-3.5 shrink-0 opacity-70" />
                            <span className="truncate">{t.entityName}</span>
                          </span>
                          <span className="flex items-center gap-1 shrink-0">
                            {t.excludeFromMigrations && (
                              <span className="text-[10px] px-1.5 py-0.5 rounded bg-amber-500/15 text-amber-600 dark:text-amber-400 border border-amber-500/25">
                                RO
                              </span>
                            )}
                            <span className="text-[11px] font-mono text-slate-400">{t.columns.length}</span>
                          </span>
                        </button>
                      );
                    })}
                  </div>
                )}
              </div>
            );
          })}

          {model.postgresEnums.length > 0 && (
            <div className="pt-3 mt-3 border-t border-slate-200 dark:border-slate-800 px-2">
              <div className="text-[11px] font-semibold uppercase tracking-wider text-slate-400 mb-1.5">
                PostgreSQL Enums ({model.postgresEnums.length})
              </div>
              <div className="space-y-1.5">
                {model.postgresEnums.map((e) => (
                  <div key={`${e.schema}.${e.name}`} className="text-xs p-2 rounded-lg bg-white dark:bg-slate-950 border border-slate-200 dark:border-slate-800">
                    <div className="font-mono font-medium text-sky-600 dark:text-sky-400">
                      {e.schema ? `${e.schema}.${e.name}` : e.name}
                    </div>
                    <div className="mt-1 flex flex-wrap gap-1">
                      {e.labels.map((l) => (
                        <span key={l} className="px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 font-mono text-[10px]">
                          {l}
                        </span>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}

          {model.sequences.length > 0 && (
            <div className="pt-3 mt-3 border-t border-slate-200 dark:border-slate-800 px-2">
              <div className="text-[11px] font-semibold uppercase tracking-wider text-slate-400 mb-1.5">
                Sequences ({model.sequences.length})
              </div>
              <div className="space-y-1.5">
                {model.sequences.map((s) => (
                  <div key={`${s.schema}.${s.name}`} className="text-xs p-2 rounded-lg bg-white dark:bg-slate-950 border border-slate-200 dark:border-slate-800">
                    <div className="font-mono font-medium text-violet-600 dark:text-violet-400 flex items-center gap-1">
                      <Hash className="w-3 h-3" />
                      {s.schema ? `${s.schema}.${s.name}` : s.name}
                    </div>
                    <div className="text-[11px] text-slate-500 mt-0.5 font-mono">
                      {s.clrTypeName} · start {s.startValue} · inc {s.incrementBy}
                      {s.isCyclic ? " · CYCLE" : ""}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      </aside>

      {/* Right Detail Pane */}
      <main className="flex-1 overflow-y-auto p-6">
        {!activeTable ? (
          <div className="h-full flex items-center justify-center text-slate-400">
            No entities found in the loaded registries.
          </div>
        ) : (
          <div className="max-w-6xl mx-auto space-y-6">
            {/* Table Header */}
            <div className="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900/70 p-5">
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div>
                  <div className="flex items-center gap-2 text-xs font-medium text-slate-500 dark:text-slate-400">
                    <span>{activeTable.registryName}</span>
                    {activeTable.excludeFromMigrations && (
                      <span className="px-2 py-0.5 rounded-full bg-amber-500/15 text-amber-600 dark:text-amber-400 border border-amber-500/30">
                        Excluded from Migrations
                      </span>
                    )}
                    {activeTable.isUnlogged && (
                      <span className="px-2 py-0.5 rounded-full bg-purple-500/15 text-purple-500 border border-purple-500/30">
                        UNLOGGED
                      </span>
                    )}
                  </div>
                  <h1 className="text-2xl font-bold tracking-tight mt-1">{activeTable.entityName}</h1>
                  <div className="mt-2 flex flex-wrap items-center gap-2 text-sm">
                    <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md bg-slate-100 dark:bg-slate-950 border border-slate-200 dark:border-slate-800 font-mono text-sky-600 dark:text-sky-400">
                      <Database className="w-3.5 h-3.5" />
                      {activeTable.qualifiedName}
                    </span>
                    {activeTable.tablespace && (
                      <span className="px-2 py-1 rounded-md bg-slate-100 dark:bg-slate-800 font-mono text-xs text-slate-600 dark:text-slate-300">
                        TABLESPACE {activeTable.tablespace}
                      </span>
                    )}
                    {activeTable.storageParameters &&
                      Object.entries(activeTable.storageParameters).map(([k, v]) => (
                        <span
                          key={k}
                          className="px-2 py-1 rounded-md bg-slate-100 dark:bg-slate-800 font-mono text-xs text-slate-600 dark:text-slate-300"
                        >
                          {k}={v}
                        </span>
                      ))}
                  </div>
                </div>

                <div className="flex gap-3 text-center">
                  <StatPill label="Columns" value={activeTable.columns.length} />
                  <StatPill label="FKs" value={activeTable.foreignKeys.length} />
                  <StatPill label="Indexes" value={activeTable.indexes.length} />
                  <StatPill
                    label="Constraints"
                    value={activeTable.checkConstraints.length + activeTable.exclusionConstraints.length}
                  />
                </div>
              </div>

              {activeTable.comment && (
                <p className="mt-4 text-sm text-slate-600 dark:text-slate-300 bg-slate-50 dark:bg-slate-950/70 border border-slate-200 dark:border-slate-800 rounded-lg px-3.5 py-2.5">
                  {activeTable.comment}
                </p>
              )}
            </div>

            {/* Columns Table */}
            <section className="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900/70 overflow-hidden">
              <div className="px-5 py-3 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between">
                <h2 className="font-semibold text-sm flex items-center gap-2">
                  <Table2 className="w-4 h-4 text-sky-500" />
                  Columns ({activeTable.columns.length})
                </h2>
                {activeTable.primaryKey && (
                  <span className="text-xs font-mono text-amber-600 dark:text-amber-400 flex items-center gap-1">
                    <KeyRound className="w-3.5 h-3.5" />
                    PK: {activeTable.primaryKey.name} ({activeTable.primaryKey.columns.join(", ")})
                  </span>
                )}
              </div>

              <div className="overflow-x-auto">
                <table className="w-full text-sm text-left">
                  <thead className="text-xs uppercase tracking-wider text-slate-500 dark:text-slate-400 bg-slate-50 dark:bg-slate-950/60 border-b border-slate-200 dark:border-slate-800">
                    <tr>
                      <th className="px-4 py-2.5">DB Column</th>
                      <th className="px-4 py-2.5">CLR Property</th>
                      <th className="px-4 py-2.5">DB Data Type</th>
                      <th className="px-4 py-2.5">Null</th>
                      <th className="px-4 py-2.5">Default / Computed</th>
                      <th className="px-4 py-2.5">Comment</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-200/70 dark:divide-slate-800/80">
                    {activeTable.columns.map((c) => (
                      <tr key={c.columnName} className="hover:bg-slate-50/80 dark:hover:bg-slate-800/30">
                        <td className="px-4 py-2.5 font-mono text-xs whitespace-nowrap">
                          <div className="flex items-center gap-1.5">
                            <span className="font-semibold text-slate-900 dark:text-slate-100">{c.columnName}</span>
                            {c.isPrimaryKey && <Badge color="amber">PK</Badge>}
                            {c.isForeignKey && <Badge color="sky">FK</Badge>}
                            {c.isIdentity && <Badge color="emerald">ID</Badge>}
                            {c.isOwnedColumn && <Badge color="violet">OWNED</Badge>}
                            {c.isRowVersion && <Badge color="rose">VER</Badge>}
                            {c.isConcurrencyToken && !c.isRowVersion && <Badge color="rose">CONC</Badge>}
                          </div>
                        </td>
                        <td className="px-4 py-2.5 font-mono text-xs text-slate-500 dark:text-slate-400 whitespace-nowrap">
                          {c.propertyName}
                          <span className="ml-1.5 text-[11px] opacity-70">({c.clrTypeName})</span>
                        </td>
                        <td className="px-4 py-2.5 font-mono text-xs text-cyan-700 dark:text-cyan-400 whitespace-nowrap">
                          {c.sqlType}
                        </td>
                        <td className="px-4 py-2.5 whitespace-nowrap">
                          {c.isNullable ? (
                            <span className="text-[11px] font-mono text-slate-400">NULL</span>
                          ) : (
                            <span className="text-[11px] font-mono font-semibold text-emerald-600 dark:text-emerald-400">
                              NOT NULL
                            </span>
                          )}
                        </td>
                        <td className="px-4 py-2.5 font-mono text-xs text-slate-600 dark:text-slate-300">
                          {c.computedColumnSql ? (
                            <span>
                              <Badge color="violet">{c.isComputedStored ? "STORED" : "VIRTUAL"}</Badge>{" "}
                              {c.computedColumnSql}
                            </span>
                          ) : c.defaultValueSql ? (
                            c.defaultValueSql
                          ) : (
                            <span className="text-slate-400">—</span>
                          )}
                        </td>
                        <td className="px-4 py-2.5 text-xs text-slate-500 dark:text-slate-400 max-w-xs">
                          {c.comment ?? <span className="opacity-40">—</span>}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>

            {/* Foreign Keys */}
            {activeTable.foreignKeys.length > 0 && (
              <section className="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900/70 overflow-hidden">
                <div className="px-5 py-3 border-b border-slate-200 dark:border-slate-800">
                  <h2 className="font-semibold text-sm flex items-center gap-2">
                    <Link2 className="w-4 h-4 text-sky-500" />
                    Foreign Keys ({activeTable.foreignKeys.length})
                  </h2>
                </div>
                <div className="divide-y divide-slate-200/70 dark:divide-slate-800/80">
                  {activeTable.foreignKeys.map((fk) => (
                    <div key={fk.name} className="px-5 py-3 flex flex-wrap items-center justify-between gap-2 text-xs">
                      <div className="font-mono">
                        <span className="font-semibold text-slate-900 dark:text-slate-100">{fk.name}</span>
                        <span className="mx-2 text-slate-400">({fk.dependentColumns.join(", ")})</span>
                        <span className="text-slate-400">→</span>
                        <button
                          onClick={() => jumpToTarget(fk.principalTable, fk.principalSchema, fk.principalEntityName)}
                          className="ml-2 text-sky-600 dark:text-sky-400 hover:underline font-semibold"
                        >
                          {fk.principalQualifiedName}({fk.principalColumns.join(", ")})
                        </button>
                      </div>
                      <div className="flex items-center gap-2">
                        <span className="px-2 py-0.5 rounded bg-slate-100 dark:bg-slate-800 font-mono text-[11px]">
                          ON DELETE {fk.onDelete.toUpperCase()}
                        </span>
                        {fk.isCircular && <Badge color="amber">DEFERRED</Badge>}
                      </div>
                    </div>
                  ))}
                </div>
              </section>
            )}

            {/* Indexes */}
            {activeTable.indexes.length > 0 && (
              <section className="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900/70 overflow-hidden">
                <div className="px-5 py-3 border-b border-slate-200 dark:border-slate-800">
                  <h2 className="font-semibold text-sm flex items-center gap-2">
                    <ListFilter className="w-4 h-4 text-violet-500" />
                    Indexes ({activeTable.indexes.length})
                  </h2>
                </div>
                <div className="divide-y divide-slate-200/70 dark:divide-slate-800/80">
                  {activeTable.indexes.map((ix) => (
                    <div key={ix.name} className="px-5 py-3 text-xs space-y-1">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div className="font-mono flex items-center gap-2">
                          <span className="font-semibold text-slate-900 dark:text-slate-100">{ix.name}</span>
                          {ix.isUnique && <Badge color="emerald">UNIQUE</Badge>}
                          {ix.method && <Badge color="violet">{ix.method.toUpperCase()}</Badge>}
                        </div>
                        <div className="font-mono text-slate-600 dark:text-slate-300">
                          (
                          {ix.columns
                            .map((col, idx) => (ix.isDescending?.[idx] ? `${col} DESC` : col))
                            .join(", ")}
                          )
                          {ix.includedColumns && ix.includedColumns.length > 0 && (
                            <span className="text-slate-400"> INCLUDE ({ix.includedColumns.join(", ")})</span>
                          )}
                        </div>
                      </div>
                      {ix.filterSql && (
                        <div className="font-mono text-amber-600 dark:text-amber-400">WHERE {ix.filterSql}</div>
                      )}
                    </div>
                  ))}
                </div>
              </section>
            )}

            {/* Check & Exclusion Constraints */}
            {(activeTable.checkConstraints.length > 0 || activeTable.exclusionConstraints.length > 0) && (
              <section className="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900/70 overflow-hidden">
                <div className="px-5 py-3 border-b border-slate-200 dark:border-slate-800">
                  <h2 className="font-semibold text-sm flex items-center gap-2">
                    <ShieldCheck className="w-4 h-4 text-emerald-500" />
                    Constraints ({activeTable.checkConstraints.length + activeTable.exclusionConstraints.length})
                  </h2>
                </div>
                <div className="divide-y divide-slate-200/70 dark:divide-slate-800/80">
                  {activeTable.checkConstraints.map((ck) => (
                    <div key={ck.name} className="px-5 py-3 text-xs font-mono space-y-1">
                      <div className="flex items-center gap-2">
                        <Badge color="emerald">CHECK</Badge>
                        <span className="font-semibold">{ck.name}</span>
                      </div>
                      <div className="text-slate-600 dark:text-slate-300 pl-1">{ck.sql}</div>
                    </div>
                  ))}
                  {activeTable.exclusionConstraints.map((ex) => (
                    <div key={ex.name} className="px-5 py-3 text-xs font-mono space-y-1">
                      <div className="flex items-center gap-2">
                        <Badge color="violet">EXCLUDE ({ex.method.toUpperCase()})</Badge>
                        <span className="font-semibold">{ex.name}</span>
                      </div>
                      <div className="text-slate-600 dark:text-slate-300 pl-1">
                        {ex.elements.join(", ")}
                        {ex.filterSql ? ` WHERE (${ex.filterSql})` : ""}
                      </div>
                    </div>
                  ))}
                </div>
              </section>
            )}

            {/* DDL Preview */}
            {activeTable.ddlPreview && (
              <section className="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900/70 overflow-hidden">
                <div className="px-5 py-3 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between">
                  <h2 className="font-semibold text-sm">Generated DDL Preview</h2>
                  <button
                    onClick={() => handleCopyDdl(activeTable.ddlPreview)}
                    className="inline-flex items-center gap-1.5 px-2.5 py-1 text-xs rounded-md border border-slate-200 dark:border-slate-700 hover:bg-slate-100 dark:hover:bg-slate-800"
                  >
                    {copiedDdl ? <Check className="w-3.5 h-3.5 text-emerald-500" /> : <Copy className="w-3.5 h-3.5" />}
                    {copiedDdl ? "Copied" : "Copy SQL"}
                  </button>
                </div>
                <pre className="p-4 text-xs font-mono overflow-x-auto bg-slate-950 text-slate-200 leading-relaxed">
                  {activeTable.ddlPreview}
                </pre>
              </section>
            )}
          </div>
        )}
      </main>
    </div>
  );
}

function StatPill({ label, value }: { label: string; value: number }) {
  return (
    <div className="px-3 py-1.5 rounded-lg bg-slate-100 dark:bg-slate-950 border border-slate-200 dark:border-slate-800">
      <div className="text-base font-bold font-mono">{value}</div>
      <div className="text-[10px] uppercase tracking-wider text-slate-500">{label}</div>
    </div>
  );
}

function Badge({
  color,
  children,
}: {
  color: "amber" | "sky" | "emerald" | "violet" | "rose";
  children: React.ReactNode;
}) {
  const styles: Record<typeof color, string> = {
    amber: "bg-amber-500/15 text-amber-600 dark:text-amber-400 border-amber-500/30",
    sky: "bg-sky-500/15 text-sky-600 dark:text-sky-400 border-sky-500/30",
    emerald: "bg-emerald-500/15 text-emerald-600 dark:text-emerald-400 border-emerald-500/30",
    violet: "bg-violet-500/15 text-violet-600 dark:text-violet-400 border-violet-500/30",
    rose: "bg-rose-500/15 text-rose-600 dark:text-rose-400 border-rose-500/30",
  };
  return (
    <span className={`px-1.5 py-0.5 text-[10px] font-semibold rounded border ${styles[color]}`}>
      {children}
    </span>
  );
}
