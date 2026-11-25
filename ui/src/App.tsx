import { useEffect, useState } from "react";
import {
  Database,
  FileCode2,
  GitFork,
  Moon,
  Stethoscope,
  Sun,
  Table2,
} from "lucide-react";
import type { UiDoctor, UiSnapshot } from "./types";
import { EntityExplorer } from "./components/EntityExplorer";
import { ErdView } from "./components/ErdView";
import { RunlistView } from "./components/RunlistView";
import { DoctorView } from "./components/DoctorView";

type Tab = "entities" | "erd" | "runlist" | "doctor";

export function App() {
  const [snapshot, setSnapshot] = useState<UiSnapshot | null>(
    () => window.__SHARP_PERSISTENCE_SNAPSHOT__ ?? null
  );
  const [isOffline] = useState(() => Boolean(window.__SHARP_PERSISTENCE_SNAPSHOT__));
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState<Tab>("entities");
  const [selectedTableKey, setSelectedTableKey] = useState<string | null>(null);
  const [dark, setDark] = useState(true);
  const [refreshingDoctor, setRefreshingDoctor] = useState(false);

  useEffect(() => {
    document.documentElement.classList.toggle("dark", dark);
  }, [dark]);

  useEffect(() => {
    if (snapshot) return;
    fetch("/api/snapshot")
      .then((res) => {
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        return res.json() as Promise<UiSnapshot>;
      })
      .then(setSnapshot)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : String(err)));
  }, [snapshot]);

  const handleRefreshDoctor = async (checkDb: boolean) => {
    if (!snapshot) return;
    setRefreshingDoctor(true);
    try {
      const res = await fetch(`/api/doctor?checkDb=${checkDb ? "true" : "false"}`);
      if (res.ok) {
        const updatedDoctor = (await res.json()) as UiDoctor;
        setSnapshot({ ...snapshot, doctor: updatedDoctor });
      }
    } finally {
      setRefreshingDoctor(false);
    }
  };

  if (error) {
    return (
      <div className="min-h-screen flex items-center justify-center p-6 bg-slate-50 dark:bg-slate-950 text-slate-900 dark:text-slate-100">
        <div className="max-w-md rounded-xl border border-rose-500/40 bg-rose-500/10 p-5 text-sm">
          <div className="font-semibold text-rose-500">Failed to load migrator snapshot</div>
          <p className="mt-1 opacity-80">{error}</p>
        </div>
      </div>
    );
  }

  if (!snapshot) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-slate-50 dark:bg-slate-950 text-slate-400 text-sm">
        Loading schema &amp; diagnostics...
      </div>
    );
  }

  const totalTables = snapshot.model.registries.reduce((sum, r) => sum + r.tables.length, 0);

  return (
    <div className="min-h-screen bg-slate-50 dark:bg-slate-950 text-slate-900 dark:text-slate-100">
      {/* Top Navigation Bar */}
      <header className="h-14 px-4 border-b border-slate-200 dark:border-slate-800 bg-white/90 dark:bg-slate-900/90 backdrop-blur flex items-center justify-between gap-4 sticky top-0 z-20">
        <div className="flex items-center gap-3 min-w-0">
          <div className="w-8 h-8 rounded-lg bg-gradient-to-br from-sky-500 to-indigo-600 flex items-center justify-center text-white shrink-0">
            <Database className="w-4 h-4" />
          </div>
          <div className="min-w-0">
            <div className="flex items-center gap-2">
              <span className="font-bold text-sm truncate">{snapshot.projectName}</span>
              <span className="px-2 py-0.5 text-[10px] font-mono font-semibold rounded-full bg-sky-500/15 text-sky-600 dark:text-sky-400 border border-sky-500/30">
                {snapshot.dialect}
              </span>
              {isOffline && (
                <span className="px-2 py-0.5 text-[10px] font-mono rounded-full bg-slate-200 dark:bg-slate-800 text-slate-500">
                  offline snapshot
                </span>
              )}
            </div>
            <div className="text-[11px] text-slate-400 font-mono truncate">
              {snapshot.model.registries.length} registries · {totalTables} entities · {snapshot.runlist.scripts.length} scripts
            </div>
          </div>
        </div>

        <nav className="flex items-center gap-1">
          <TabButton
            active={tab === "entities"}
            onClick={() => setTab("entities")}
            icon={<Table2 className="w-4 h-4" />}
            label="Entities"
          />
          <TabButton
            active={tab === "erd"}
            onClick={() => setTab("erd")}
            icon={<GitFork className="w-4 h-4" />}
            label="ERD"
          />
          <TabButton
            active={tab === "runlist"}
            onClick={() => setTab("runlist")}
            icon={<FileCode2 className="w-4 h-4" />}
            label="Runlist"
          />
          <TabButton
            active={tab === "doctor"}
            onClick={() => setTab("doctor")}
            icon={<Stethoscope className="w-4 h-4" />}
            label="Doctor"
            badge={
              snapshot.doctor.errorCount > 0
                ? { count: snapshot.doctor.errorCount, tone: "rose" }
                : snapshot.doctor.warningCount > 0
                  ? { count: snapshot.doctor.warningCount, tone: "amber" }
                  : undefined
            }
          />
        </nav>

        <button
          onClick={() => setDark((d) => !d)}
          className="p-2 rounded-lg border border-slate-200 dark:border-slate-700 hover:bg-slate-100 dark:hover:bg-slate-800"
          title="Toggle theme"
        >
          {dark ? <Sun className="w-4 h-4 text-amber-400" /> : <Moon className="w-4 h-4 text-slate-600" />}
        </button>
      </header>

      {tab === "entities" && (
        <EntityExplorer
          model={snapshot.model}
          selectedKey={selectedTableKey}
          onSelectTable={setSelectedTableKey}
        />
      )}

      {tab === "erd" && (
        <ErdView
          model={snapshot.model}
          onInspectTable={(key) => {
            setSelectedTableKey(key);
            setTab("entities");
          }}
        />
      )}

      {tab === "runlist" && (
        <RunlistView runlist={snapshot.runlist} isOfflineSnapshot={isOffline} />
      )}

      {tab === "doctor" && (
        <DoctorView
          doctor={snapshot.doctor}
          isOfflineSnapshot={isOffline}
          isRefreshing={refreshingDoctor}
          onRefresh={handleRefreshDoctor}
        />
      )}
    </div>
  );
}

function TabButton({
  active,
  onClick,
  icon,
  label,
  badge,
}: {
  active: boolean;
  onClick: () => void;
  icon: React.ReactNode;
  label: string;
  badge?: { count: number; tone: "rose" | "amber" };
}) {
  return (
    <button
      onClick={onClick}
      className={`px-3 py-1.5 rounded-lg text-xs font-medium flex items-center gap-1.5 transition ${
        active
          ? "bg-sky-500/15 text-sky-600 dark:text-sky-400 border border-sky-500/30"
          : "text-slate-600 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800 border border-transparent"
      }`}
    >
      {icon}
      <span>{label}</span>
      {badge && (
        <span
          className={`px-1.5 py-0.2 rounded-full text-[10px] font-mono font-bold ${
            badge.tone === "rose"
              ? "bg-rose-500 text-white"
              : "bg-amber-500 text-slate-950"
          }`}
        >
          {badge.count}
        </span>
      )}
    </button>
  );
}
