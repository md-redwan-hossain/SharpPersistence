import { useState } from "react";
import { FileCode2, GitCompareArrows } from "lucide-react";
import type { UiDryRun } from "../schemas/apiSchemas";
import { Badge } from "./ui/badge";
import { RunlistView } from "./RunlistView";
import { DryRunView } from "./DryRunView";

type SubTab = "runlist" | "dry-run";

interface Props {
  runlistCount: number;
  dryRun: UiDryRun | null;
  onDryRunLoaded: (data: UiDryRun) => void;
}

export function MigrationsView({ runlistCount, dryRun, onDryRunLoaded }: Props) {
  const [subTab, setSubTab] = useState<SubTab>("runlist");

  return (
    <div className="flex flex-col">
      {/* Sub-navigation bar */}
      <div className="px-3 sm:px-4 py-2 border-b border-border bg-muted/30 flex flex-wrap items-center justify-between gap-2">
        <div className="inline-flex items-center rounded-lg bg-muted p-0.5 border border-border">
          <button
            onClick={() => setSubTab("runlist")}
            className={`inline-flex items-center gap-1.5 px-3 py-1 rounded-md text-xs font-medium transition cursor-pointer ${
              subTab === "runlist"
                ? "bg-background text-foreground shadow-xs"
                : "text-muted-foreground hover:text-foreground"
            }`}
          >
            <FileCode2 className="w-3.5 h-3.5" />
            Runlist
            <Badge variant="secondary" className="ml-0.5 px-1.5 py-0 text-[10px]">
              {runlistCount}
            </Badge>
          </button>

          <button
            onClick={() => setSubTab("dry-run")}
            className={`inline-flex items-center gap-1.5 px-3 py-1 rounded-md text-xs font-medium transition cursor-pointer ${
              subTab === "dry-run"
                ? "bg-background text-foreground shadow-xs"
                : "text-muted-foreground hover:text-foreground"
            }`}
          >
            <GitCompareArrows className="w-3.5 h-3.5" />
            Dry Run
            {dryRun && dryRun.changedRegistryCount > 0 && (
              <Badge
                variant={dryRun.dropCount > 0 ? "destructive" : "warning"}
                className="ml-0.5 px-1.5 py-0 text-[10px]"
              >
                {dryRun.changedRegistryCount}
              </Badge>
            )}
          </button>
        </div>

        <span className="text-[11px] text-muted-foreground hidden sm:inline">
          {subTab === "runlist"
            ? "Effective migration script execution order"
            : "Read-only preview of pending changes vs. EntityChangelogs snapshots"}
        </span>
      </div>

      {subTab === "runlist" ? (
        <RunlistView />
      ) : (
        <DryRunView dryRun={dryRun} onLoaded={onDryRunLoaded} />
      )}
    </div>
  );
}
