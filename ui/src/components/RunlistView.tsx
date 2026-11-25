import { useEffect, useMemo, useState } from "react";
import { AlertTriangle, Check, Copy, FileCode2, Search } from "lucide-react";
import type { UiRunlist, UiRunlistScript } from "../types";

interface Props {
  runlist: UiRunlist;
  isOfflineSnapshot: boolean;
}

export function RunlistView({ runlist, isOfflineSnapshot }: Props) {
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(runlist.scripts[0]?.id ?? null);
  const [sqlCache, setSqlCache] = useState<Record<string, string>>({});
  const [loadingSql, setLoadingSql] = useState(false);
  const [copied, setCopied] = useState(false);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return runlist.scripts;
    return runlist.scripts.filter(
      (s) =>
        s.scriptName.toLowerCase().includes(q) ||
        s.registryName.toLowerCase().includes(q) ||
        s.id.toLowerCase().includes(q)
    );
  }, [runlist.scripts, search]);

  const activeScript: UiRunlistScript | null = useMemo(() => {
    if (selectedId) {
      const match = runlist.scripts.find((s) => s.id === selectedId);
      if (match) return match;
    }
    return runlist.scripts[0] ?? null;
  }, [runlist.scripts, selectedId]);

  useEffect(() => {
    if (!activeScript) return;
    if (activeScript.sql !== null || sqlCache[activeScript.id] !== undefined || isOfflineSnapshot) {
      return;
    }

    let cancelled = false;
    setLoadingSql(true);
    fetch(`/api/runlist/${encodeURIComponent(activeScript.id)}/sql`)
      .then((res) => (res.ok ? res.text() : "-- Failed to load SQL script"))
      .then((text) => {
        if (!cancelled) {
          setSqlCache((prev) => ({ ...prev, [activeScript.id]: text }));
        }
      })
      .finally(() => {
        if (!cancelled) setLoadingSql(false);
      });

    return () => {
      cancelled = true;
    };
  }, [activeScript, sqlCache, isOfflineSnapshot]);

  const activeSql = activeScript ? activeScript.sql ?? sqlCache[activeScript.id] ?? "" : "";

  const handleCopy = async () => {
    if (!activeSql) return;
    try {
      await navigator.clipboard.writeText(activeSql);
      setCopied(true);
      setTimeout(() => setCopied(false), 1600);
    } catch {
      // Clipboard unavailable
    }
  };

  if (runlist.error) {
    return (
      <div className="p-6 max-w-3xl mx-auto">
        <div className="rounded-xl border border-amber-500/40 bg-amber-500/10 p-4 flex items-start gap-3">
          <AlertTriangle className="w-5 h-5 text-amber-500 shrink-0 mt-0.5" />
          <div className="text-sm">
            <div className="font-semibold text-amber-600 dark:text-amber-400">Runlist Error</div>
            <p className="mt-1 text-slate-700 dark:text-slate-300">{runlist.error}</p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex h-[calc(100vh-3.5rem)] overflow-hidden">
      {/* Left List */}
      <aside className="w-96 shrink-0 border-r border-slate-200 dark:border-slate-800 bg-slate-50/70 dark:bg-slate-900/50 flex flex-col">
        <div className="p-3 border-b border-slate-200 dark:border-slate-800">
          <div className="relative">
            <Search className="w-4 h-4 text-slate-400 absolute left-2.5 top-2.5" />
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Filter scripts, registry, UUIDv7..."
              className="w-full pl-8 pr-3 py-1.5 text-sm rounded-lg bg-white dark:bg-slate-950 border border-slate-200 dark:border-slate-800 focus:outline-none focus:ring-2 focus:ring-sky-500/40"
            />
          </div>
        </div>

        <div className="flex-1 overflow-y-auto divide-y divide-slate-200/70 dark:divide-slate-800/80">
          {filtered.length === 0 ? (
            <div className="p-6 text-center text-sm text-slate-400">No migration scripts found.</div>
          ) : (
            filtered.map((s) => {
              const isActive = activeScript?.id === s.id;
              return (
                <button
                  key={s.id}
                  onClick={() => setSelectedId(s.id)}
                  className={`w-full text-left px-4 py-3 transition ${
                    isActive
                      ? "bg-sky-500/15 border-l-2 border-l-sky-500"
                      : "hover:bg-slate-200/50 dark:hover:bg-slate-800/40"
                  }`}
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-xs font-mono text-slate-400">#{s.order}</span>
                    <div className="flex items-center gap-1.5">
                      <span className="text-[10px] px-1.5 py-0.5 rounded bg-slate-200/80 dark:bg-slate-800 font-mono">
                        {s.registryName}
                      </span>
                      {s.isOverridden && (
                        <span className="text-[10px] px-1.5 py-0.5 rounded bg-amber-500/15 text-amber-600 dark:text-amber-400 border border-amber-500/30">
                          override
                        </span>
                      )}
                    </div>
                  </div>
                  <div className="mt-1 text-sm font-mono font-medium truncate text-slate-900 dark:text-slate-100">
                    {s.scriptName}
                  </div>
                  <div className="mt-0.5 text-[11px] font-mono text-slate-400 truncate">{s.id}</div>
                </button>
              );
            })
          )}
        </div>
      </aside>

      {/* Right SQL Viewer */}
      <main className="flex-1 flex flex-col overflow-hidden bg-slate-950 text-slate-100">
        {!activeScript ? (
          <div className="flex-1 flex items-center justify-center text-slate-500">
            Select a migration script to inspect its SQL.
          </div>
        ) : (
          <>
            <div className="px-5 py-3 border-b border-slate-800 bg-slate-900/80 flex items-center justify-between gap-4">
              <div className="min-w-0">
                <div className="flex items-center gap-2 text-sm font-mono font-semibold truncate">
                  <FileCode2 className="w-4 h-4 text-sky-400 shrink-0" />
                  <span>
                    {activeScript.registryName}/{activeScript.scriptName}
                  </span>
                </div>
                <div className="text-xs font-mono text-slate-400 mt-0.5">
                  #{activeScript.order} · {activeScript.id}
                </div>
              </div>
              <button
                onClick={handleCopy}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs rounded-md border border-slate-700 hover:bg-slate-800 shrink-0"
              >
                {copied ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
                {copied ? "Copied" : "Copy SQL"}
              </button>
            </div>
            <div className="flex-1 overflow-auto p-4 font-mono text-xs leading-relaxed">
              {loadingSql ? (
                <div className="text-slate-400">Loading SQL...</div>
              ) : (
                <table className="w-full border-collapse">
                  <tbody>
                    {activeSql.split(/\r?\n/).map((line, i) => (
                      <tr key={i} className="hover:bg-slate-900/70">
                        <td className="select-none pr-4 text-right text-slate-600 w-10 align-top">{i + 1}</td>
                        <td className="whitespace-pre text-slate-200">{line}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </>
        )}
      </main>
    </div>
  );
}
