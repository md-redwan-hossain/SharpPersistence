import { useEffect, useRef, useState } from "react";
import { CheckCircle2, Code2, Loader2, RefreshCw, X, XCircle } from "lucide-react";
import { API_ROUTES } from "../lib/apiRoutes";
import {
  fetchValidatedJson,
  postValidatedJson,
  UiDdlCheckResultSchema,
  UiScriptDdlsSchema,
  UiScriptLiveCheckSchema,
  type UiDdlCheckResult,
  type UiDdlStatement,
} from "../schemas/apiSchemas";
import { Badge } from "./ui/badge";
import { Button } from "./ui/button";
import { SqlCodeBlock } from "./ui/sql-code-block";

interface Props {
  scriptId: string;
}

export function RunlistDdlPanel({ scriptId }: Props) {
  const [ddlCache, setDdlCache] = useState<Record<string, UiDdlStatement[]>>({});
  const [results, setResults] = useState<Record<number, UiDdlCheckResult>>({});
  const [checkingRow, setCheckingRow] = useState<Record<number, boolean>>({});
  const [checkingAll, setCheckingAll] = useState(false);
  const [allError, setAllError] = useState<string | null>(null);
  const [historyStatus, setHistoryStatus] = useState<boolean | null>(null);
  const [elapsedMs, setElapsedMs] = useState<number | null>(null);
  const [modalStmt, setModalStmt] = useState<UiDdlStatement | null>(null);
  const activeScriptRef = useRef(scriptId);

  const statements = ddlCache[scriptId] ?? [];

  useEffect(() => {
    activeScriptRef.current = scriptId;
    setResults({});
    setCheckingRow({});
    setCheckingAll(false);
    setAllError(null);
    setHistoryStatus(null);
    setElapsedMs(null);
    setModalStmt(null);
  }, [scriptId]);

  useEffect(() => {
    if (ddlCache[scriptId] !== undefined) return;

    const controller = new AbortController();
    fetchValidatedJson(API_ROUTES.runlistDdls(scriptId), UiScriptDdlsSchema, controller.signal)
      .then((data) => {
        setDdlCache((prev) => ({ ...prev, [scriptId]: data.statements }));
      })
      .catch(() => {});

    return () => controller.abort();
  }, [scriptId, ddlCache]);

  useEffect(() => {
    if (!modalStmt) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setModalStmt(null);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [modalStmt]);

  const handleCheckOne = async (stmt: UiDdlStatement) => {
    const targetScriptId = scriptId;
    setCheckingRow((prev) => ({ ...prev, [stmt.index]: true }));
    setAllError(null);
    try {
      const res = await postValidatedJson(
        API_ROUTES.ddlCheckLive,
        {
          action: stmt.action,
          objectType: stmt.objectType,
          schema: stmt.schema,
          tableName: stmt.tableName,
          objectName: stmt.objectName,
          target: stmt.target,
        },
        UiDdlCheckResultSchema
      );
      if (activeScriptRef.current !== targetScriptId) return;
      setResults((prev) => ({ ...prev, [stmt.index]: res }));
    } catch (err: unknown) {
      if (activeScriptRef.current !== targetScriptId) return;
      const msg = err instanceof Error ? err.message : String(err);
      setResults((prev) => ({
        ...prev,
        [stmt.index]: { index: stmt.index, target: stmt.target, passed: false, existsInDb: false, detail: msg, error: msg },
      }));
    } finally {
      if (activeScriptRef.current === targetScriptId) {
        setCheckingRow((prev) => ({ ...prev, [stmt.index]: false }));
      }
    }
  };

  const handleCheckAll = async () => {
    const targetScriptId = scriptId;
    setCheckingAll(true);
    setAllError(null);
    try {
      const data = await fetchValidatedJson(API_ROUTES.runlistCheckLive(targetScriptId), UiScriptLiveCheckSchema);
      if (activeScriptRef.current !== targetScriptId) return;
      setElapsedMs(data.elapsedMs);
      setHistoryStatus(data.isAppliedInHistory);
      if (!data.connected || data.error) {
        setAllError(data.error ?? "Could not connect to database.");
        return;
      }
      const next: Record<number, UiDdlCheckResult> = {};
      for (const r of data.results) {
        if (r.index !== null) next[r.index] = r;
      }
      setResults(next);
    } catch (err: unknown) {
      if (activeScriptRef.current !== targetScriptId) return;
      setAllError(err instanceof Error ? err.message : String(err));
    } finally {
      if (activeScriptRef.current === targetScriptId) {
        setCheckingAll(false);
      }
    }
  };

  if (statements.length === 0) return null;

  return (
    <>
      <div className="border-b border-border bg-muted/20">
        <div className="px-4 sm:px-5 py-2.5 flex flex-wrap items-center justify-between gap-2 border-b border-border/70">
          <div className="flex items-center gap-2 text-xs font-semibold">
            <span>DDL Operations ({statements.length})</span>
            {historyStatus !== null && (
              <Badge variant={historyStatus ? "success" : "warning"}>
                {historyStatus ? "In history" : "Not in history"}
              </Badge>
            )}
            {elapsedMs !== null && (
              <span className="text-[11px] font-mono font-normal text-muted-foreground">{elapsedMs} ms</span>
            )}
          </div>
          <Button variant="outline" size="sm" disabled={checkingAll} onClick={handleCheckAll}>
            <RefreshCw className={`w-3.5 h-3.5 ${checkingAll ? "animate-spin" : ""}`} />
            Check All
          </Button>
        </div>

        {allError && (
          <div className="px-4 sm:px-5 py-2 text-xs text-rose-600 dark:text-rose-400 bg-rose-500/5 border-b border-rose-500/20">
            {allError}
          </div>
        )}

        <div className="max-h-56 overflow-y-auto divide-y divide-border/60">
          {statements.map((stmt) => {
            const res = results[stmt.index];
            const busy = Boolean(checkingRow[stmt.index]) || checkingAll;
            return (
              <div key={stmt.index} className="px-4 sm:px-5 py-2 flex items-center justify-between gap-3 text-xs">
                <div className="flex items-center gap-2 min-w-0">
                  <Badge variant={stmt.action === "DROP" ? "destructive" : "success"} className="w-16 justify-center shrink-0">
                    {stmt.action}
                  </Badge>
                  <Badge variant="outline" className="shrink-0">
                    {stmt.objectType}
                  </Badge>
                  <span className="font-mono font-medium truncate text-foreground">{stmt.target}</span>
                </div>

                <div className="flex items-center gap-2 shrink-0">
                  {res && (
                    <span title={res.error ?? res.detail} className="inline-flex items-center">
                      {res.passed ? (
                        <CheckCircle2 className="w-4 h-4 text-emerald-500" />
                      ) : (
                        <XCircle className="w-4 h-4 text-rose-500" />
                      )}
                    </span>
                  )}
                  <Button variant="ghost" size="sm" onClick={() => setModalStmt(stmt)}>
                    <Code2 className="w-3.5 h-3.5" />
                    SQL
                  </Button>
                  <Button variant="outline" size="sm" disabled={busy} onClick={() => handleCheckOne(stmt)}>
                    {busy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : "Check"}
                  </Button>
                </div>
              </div>
            );
          })}
        </div>
      </div>

      {modalStmt && (
        <div
          className="fixed inset-0 z-50 bg-black/50 flex items-center justify-center p-4"
          onClick={() => setModalStmt(null)}
        >
          <div
            className="bg-card border border-border rounded-lg shadow-xl w-full max-w-3xl max-h-[80vh] flex flex-col overflow-hidden"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="px-4 py-3 border-b border-border flex items-center justify-between gap-2">
              <div className="flex items-center gap-2 text-xs min-w-0">
                <Badge variant={modalStmt.action === "DROP" ? "destructive" : "success"}>{modalStmt.action}</Badge>
                <Badge variant="outline">{modalStmt.objectType}</Badge>
                <span className="font-mono font-semibold truncate">{modalStmt.target}</span>
              </div>
              <Button variant="ghost" size="icon" onClick={() => setModalStmt(null)} aria-label="Close">
                <X className="w-4 h-4" />
              </Button>
            </div>
            <div className="flex-1 overflow-auto">
              <SqlCodeBlock code={modalStmt.sql} maxHeightClass="max-h-[65vh]" />
            </div>
          </div>
        </div>
      )}
    </>
  );
}
