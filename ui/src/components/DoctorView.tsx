import { AlertTriangle, CheckCircle2, Database, RefreshCw, XCircle } from "lucide-react";
import type { UiDoctor, UiDoctorItem } from "../types";

interface Props {
  doctor: UiDoctor;
  isOfflineSnapshot: boolean;
  isRefreshing: boolean;
  onRefresh: (checkDb: boolean) => void;
}

export function DoctorView({ doctor, isOfflineSnapshot, isRefreshing, onRefresh }: Props) {
  return (
    <div className="h-[calc(100vh-3.5rem)] overflow-y-auto p-6">
      <div className="max-w-5xl mx-auto space-y-6">
        {/* Top Summary Bar */}
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div className="grid grid-cols-3 gap-3">
            <SummaryCard label="Passed" value={doctor.passedCount} tone="emerald" />
            <SummaryCard label="Warnings" value={doctor.warningCount} tone="amber" />
            <SummaryCard label="Errors" value={doctor.errorCount} tone="rose" />
          </div>

          {!isOfflineSnapshot && (
            <div className="flex items-center gap-2">
              <button
                disabled={isRefreshing}
                onClick={() => onRefresh(false)}
                className="inline-flex items-center gap-1.5 px-3 py-2 text-xs font-medium rounded-lg border border-slate-300 dark:border-slate-700 hover:bg-slate-100 dark:hover:bg-slate-800 disabled:opacity-50"
              >
                <RefreshCw className={`w-3.5 h-3.5 ${isRefreshing ? "animate-spin" : ""}`} />
                Re-run Offline
              </button>
              <button
                disabled={isRefreshing}
                onClick={() => onRefresh(true)}
                className="inline-flex items-center gap-1.5 px-3.5 py-2 text-xs font-medium rounded-lg bg-sky-600 text-white hover:bg-sky-500 disabled:opacity-50"
              >
                <Database className="w-3.5 h-3.5" />
                {isRefreshing ? "Checking..." : "Run Live DB Check"}
              </button>
            </div>
          )}
        </div>

        <CheckSection title="DI & Wiring" items={doctor.wiringChecks} />

        {doctor.registryReports.map((reg) => (
          <CheckSection
            key={reg.registryName}
            title={`Registry: ${reg.registryName}`}
            subtitle={`${reg.entityCount} entities${reg.excludedEntityCount > 0 ? ` (${reg.excludedEntityCount} excluded)` : ""} · ${reg.columnCount} cols · ${reg.foreignKeyCount} FKs`}
            items={reg.items}
          />
        ))}

        {doctor.crossRegistryChecks.length > 0 && (
          <CheckSection title="Cross-Registry Checks" items={doctor.crossRegistryChecks} />
        )}

        {doctor.migrationCoverageChecks.length > 0 && (
          <CheckSection title="Migration Scripts & Runlist" items={doctor.migrationCoverageChecks} />
        )}

        {doctor.databaseChecks.length > 0 && (
          <CheckSection title="Live Database" items={doctor.databaseChecks} />
        )}
      </div>
    </div>
  );
}

function SummaryCard({
  label,
  value,
  tone,
}: {
  label: string;
  value: number;
  tone: "emerald" | "amber" | "rose";
}) {
  const colors = {
    emerald: "border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400",
    amber: "border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400",
    rose: "border-rose-500/30 bg-rose-500/10 text-rose-600 dark:text-rose-400",
  }[tone];

  return (
    <div className={`px-5 py-3 rounded-xl border ${colors} min-w-28`}>
      <div className="text-2xl font-bold font-mono">{value}</div>
      <div className="text-[11px] uppercase tracking-wider font-semibold opacity-80">{label}</div>
    </div>
  );
}

function CheckSection({
  title,
  subtitle,
  items,
}: {
  title: string;
  subtitle?: string;
  items: UiDoctorItem[];
}) {
  return (
    <section className="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900/70 overflow-hidden">
      <div className="px-5 py-3 border-b border-slate-200 dark:border-slate-800 flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-semibold text-sm">{title}</h2>
        {subtitle && <span className="text-xs font-mono text-slate-400">{subtitle}</span>}
      </div>
      {items.length === 0 ? (
        <div className="px-5 py-3 text-xs text-slate-400">No issues reported.</div>
      ) : (
        <div className="divide-y divide-slate-200/70 dark:divide-slate-800/80">
          {items.map((item, idx) => (
            <div key={idx} className="px-5 py-3 flex items-start gap-3 text-sm">
              {item.severity === "Pass" && <CheckCircle2 className="w-4 h-4 text-emerald-500 shrink-0 mt-0.5" />}
              {item.severity === "Warning" && <AlertTriangle className="w-4 h-4 text-amber-500 shrink-0 mt-0.5" />}
              {item.severity === "Error" && <XCircle className="w-4 h-4 text-rose-500 shrink-0 mt-0.5" />}
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-2">
                  {item.entityName && (
                    <span className="px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 font-mono text-[11px] font-semibold">
                      {item.entityName}
                    </span>
                  )}
                  <span className="text-slate-800 dark:text-slate-200 text-xs">{item.message}</span>
                </div>
                {item.hint && (
                  <div className="mt-1 text-xs text-slate-500 dark:text-slate-400">→ {item.hint}</div>
                )}
              </div>
            </div>
          ))}
        </div>
      )}
    </section>
  );
}
