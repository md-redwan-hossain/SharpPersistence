import { useEffect, useState } from "react";
import { AlertTriangle, Check, CheckCircle2, Database, Loader2, RefreshCw, X, XCircle } from "lucide-react";
import { API_ROUTES } from "../lib/apiRoutes";
import {
  fetchValidatedJson,
  UiDoctorSchema,
  type UiDoctor,
  type UiDoctorItem,
} from "../schemas/apiSchemas";
import { Badge } from "./ui/badge";
import { Button } from "./ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "./ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "./ui/table";

interface Props {
  doctor: UiDoctor | null;
  onDoctorLoaded: (doctor: UiDoctor) => void;
}

export function DoctorView({ doctor, onDoctorLoaded }: Props) {
  const [loading, setLoading] = useState(doctor === null);
  const [error, setError] = useState<string | null>(null);

  const runCheck = async (checkDb: boolean, signal?: AbortSignal) => {
    setLoading(true);
    setError(null);
    try {
      const data = await fetchValidatedJson(
        API_ROUTES.doctor(checkDb),
        UiDoctorSchema,
        signal
      );
      onDoctorLoaded(data);
    } catch (err: unknown) {
      if (signal?.aborted) return;
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  };

  useEffect(() => {
    if (doctor !== null) return;
    const controller = new AbortController();
    void runCheck(false, controller.signal);
    return () => controller.abort();
  }, [doctor]);

  if (loading && !doctor) {
    return (
      <div className="h-64 flex items-center justify-center gap-2 text-xs text-muted-foreground">
        <Loader2 className="w-4 h-4 animate-spin" />
        Running doctor checks...
      </div>
    );
  }

  if (error && !doctor) {
    return (
      <div className="p-4 sm:p-6 max-w-3xl mx-auto">
        <Card className="border-rose-500/40">
          <CardContent className="text-xs text-rose-600 dark:text-rose-400">{error}</CardContent>
        </Card>
      </div>
    );
  }

  if (!doctor) return null;

  const registryIssues = doctor.registryReports.flatMap((reg) =>
    reg.items
      .filter((i) => i.severity !== "Pass")
      .map((item) => ({ registryName: reg.registryName, item }))
  );

  return (
    <div className="lg:h-[calc(100vh-3.5rem)] overflow-y-auto p-3 sm:p-6">
      <div className="max-w-5xl mx-auto space-y-5">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div className="grid grid-cols-3 gap-2 sm:gap-3 w-full sm:w-auto">
            <SummaryCard label="Passed" value={doctor.passedCount} tone="emerald" />
            <SummaryCard label="Warnings" value={doctor.warningCount} tone="amber" />
            <SummaryCard label="Errors" value={doctor.errorCount} tone="rose" />
          </div>

          <div className="flex items-center gap-2">
            <Button variant="outline" disabled={loading} onClick={() => void runCheck(false)}>
              <RefreshCw className={`w-3.5 h-3.5 ${loading ? "animate-spin" : ""}`} />
              Run Offline Check
            </Button>
            <Button disabled={loading} onClick={() => void runCheck(true)}>
              <Database className="w-3.5 h-3.5" />
              {loading ? "Checking..." : "Run Live DB Check"}
            </Button>
          </div>
        </div>

        {error && (
          <Card className="border-rose-500/40">
            <CardContent className="text-xs text-rose-600 dark:text-rose-400">{error}</CardContent>
          </Card>
        )}

        <CheckSection title="DI & Wiring" items={doctor.wiringChecks} />

        {/* Registries Summary Table */}
        <Card className="overflow-hidden">
          <CardHeader>
            <CardTitle>Registries ({doctor.registryReports.length})</CardTitle>
          </CardHeader>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Registry</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Entities</TableHead>
                <TableHead>Columns</TableHead>
                <TableHead>Foreign Keys</TableHead>
                <TableHead>Constraints</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {doctor.registryReports.map((reg) => {
                const errors = reg.items.filter((i) => i.severity === "Error");
                const warnings = reg.items.filter((i) => i.severity === "Warning");

                const invalidEntities = new Set(
                  errors.map((i) => i.entityName).filter((n): n is string => Boolean(n))
                ).size;
                const invalidFks = errors.filter((i) => /\b(fk|foreign key|principal)\b/i.test(i.message)).length;
                const invalidConstraints = errors.filter((i) => /\b(constraint|check|exclusion)\b/i.test(i.message)).length;
                const invalidCols = errors.filter(
                  (i) =>
                    /\b(column|computed|property|type)\b/i.test(i.message) &&
                    !/\b(fk|foreign key|principal|constraint|check|exclusion)\b/i.test(i.message)
                ).length;

                return (
                  <TableRow key={reg.registryName}>
                    <TableCell className="font-mono font-semibold whitespace-nowrap">
                      <div className="flex items-center gap-1.5">
                        <span>{reg.registryName}</span>
                        {reg.excludedEntityCount > 0 && (
                          <Badge variant="warning">{reg.excludedEntityCount} excluded</Badge>
                        )}
                      </div>
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      {errors.length > 0 ? (
                        <Badge variant="destructive">{errors.length} Error{errors.length === 1 ? "" : "s"}</Badge>
                      ) : warnings.length > 0 ? (
                        <Badge variant="warning">{warnings.length} Warning{warnings.length === 1 ? "" : "s"}</Badge>
                      ) : (
                        <Badge variant="success">Valid</Badge>
                      )}
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      <ValidInvalidCell total={reg.entityCount} invalid={invalidEntities} />
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      <ValidInvalidCell total={reg.columnCount} invalid={invalidCols} />
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      <ValidInvalidCell total={reg.foreignKeyCount} invalid={invalidFks} />
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      <ValidInvalidCell total={reg.constraintCount} invalid={invalidConstraints} />
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </Card>

        {registryIssues.length > 0 && (
          <Card className="overflow-hidden">
            <CardHeader>
              <CardTitle>Registry Issues ({registryIssues.length})</CardTitle>
            </CardHeader>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Severity</TableHead>
                  <TableHead>Registry</TableHead>
                  <TableHead>Entity</TableHead>
                  <TableHead>Details</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {registryIssues.map(({ registryName, item }, idx) => (
                  <TableRow key={idx}>
                    <TableCell className="whitespace-nowrap">
                      {item.severity === "Error" ? (
                        <Badge variant="destructive">Error</Badge>
                      ) : (
                        <Badge variant="warning">Warning</Badge>
                      )}
                    </TableCell>
                    <TableCell className="font-mono whitespace-nowrap">{registryName}</TableCell>
                    <TableCell className="font-mono whitespace-nowrap">
                      {item.entityName ?? <span className="opacity-40">{"\u2014"}</span>}
                    </TableCell>
                    <TableCell className="text-xs">
                      <div className="text-foreground break-words">{item.message}</div>
                      {item.hint && (
                        <div className="mt-0.5 text-muted-foreground break-words">
                          {"\u2192"} {item.hint}
                        </div>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Card>
        )}

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

function ValidInvalidCell({ total, invalid }: { total: number; invalid: number }) {
  const valid = Math.max(0, total - invalid);
  return (
    <span className="inline-flex items-center gap-1.5 font-mono text-xs tabular-nums">
      <span className="inline-flex items-center gap-0.5 text-emerald-600 dark:text-emerald-400 font-semibold">
        <Check className="w-3.5 h-3.5" />
        {valid}
      </span>
      <span className="text-muted-foreground/50">/</span>
      <span
        className={`inline-flex items-center gap-0.5 ${
          invalid > 0 ? "text-rose-600 dark:text-rose-400 font-semibold" : "text-muted-foreground"
        }`}
      >
        <X className="w-3.5 h-3.5" />
        {invalid}
      </span>
    </span>
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
    emerald: "border-emerald-500/30 bg-emerald-500/10 text-emerald-700 dark:text-emerald-400",
    amber: "border-amber-500/30 bg-amber-500/10 text-amber-700 dark:text-amber-400",
    rose: "border-rose-500/30 bg-rose-500/10 text-rose-700 dark:text-rose-400",
  }[tone];

  return (
    <div className={`px-4 py-2.5 rounded-xl border ${colors} min-w-24`}>
      <div className="text-xl font-bold font-mono tabular-nums">{value}</div>
      <div className="text-[10px] uppercase tracking-wider font-semibold opacity-80">{label}</div>
    </div>
  );
}

function CheckSection({
  title,
  items,
}: {
  title: string;
  items: UiDoctorItem[];
}) {
  return (
    <Card className="overflow-hidden">
      <CardHeader>
        <CardTitle>{title}</CardTitle>
      </CardHeader>
      {items.length === 0 ? (
        <div className="px-4 sm:px-5 py-3 text-xs text-muted-foreground">No issues reported.</div>
      ) : (
        <div className="divide-y divide-border/70">
          {items.map((item, idx) => (
            <div key={idx} className="px-4 sm:px-5 py-3 flex items-start gap-3 text-xs">
              {item.severity === "Pass" && <CheckCircle2 className="w-4 h-4 text-emerald-500 shrink-0 mt-0.5" />}
              {item.severity === "Warning" && <AlertTriangle className="w-4 h-4 text-amber-500 shrink-0 mt-0.5" />}
              {item.severity === "Error" && <XCircle className="w-4 h-4 text-rose-500 shrink-0 mt-0.5" />}
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-2">
                  {item.entityName && <Badge variant="secondary">{item.entityName}</Badge>}
                  <span className="text-foreground break-words">{item.message}</span>
                </div>
                {item.hint && (
                  <div className="mt-1 text-muted-foreground break-words">
                    {"\u2192"} {item.hint}
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>
      )}
    </Card>
  );
}
