import { useMemo, useState } from "react";
import { ZoomIn, ZoomOut, RotateCcw } from "lucide-react";
import type { UiModel, UiTable } from "../types";
import { tableKey } from "./EntityExplorer";

interface Props {
  model: UiModel;
  onInspectTable: (key: string) => void;
}

const REGISTRY_COLORS = [
  "#0ea5e9",
  "#8b5cf6",
  "#10b981",
  "#f59e0b",
  "#ec4899",
  "#14b8a6",
  "#f97316",
  "#6366f1",
];

const CARD_W = 240;
const HEADER_H = 44;
const ROW_H = 22;
const MAX_ROWS = 10;
const GAP_X = 90;
const GAP_Y = 70;

export function ErdView({ model, onInspectTable }: Props) {
  const [activeRegistry, setActiveRegistry] = useState<string>("ALL");
  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 40, y: 40 });
  const [dragging, setDragging] = useState<{ startX: number; startY: number; origX: number; origY: number } | null>(null);

  const registryColorMap = useMemo(() => {
    const map = new Map<string, string>();
    model.registries.forEach((r, idx) => {
      map.set(r.name, REGISTRY_COLORS[idx % REGISTRY_COLORS.length]);
    });
    return map;
  }, [model.registries]);

  const tables = useMemo(() => {
    if (activeRegistry === "ALL") {
      return model.registries.flatMap((r) => r.tables);
    }
    return model.registries.find((r) => r.name === activeRegistry)?.tables ?? [];
  }, [model.registries, activeRegistry]);

  const layout = useMemo(() => {
    const cols = Math.max(2, Math.ceil(Math.sqrt(tables.length)));
    const positions = new Map<string, { x: number; y: number; h: number; table: UiTable }>();

    let rowY = 0;
    for (let r = 0; r < Math.ceil(tables.length / cols); r++) {
      let maxRowH = 0;
      for (let c = 0; c < cols; c++) {
        const idx = r * cols + c;
        if (idx >= tables.length) break;
        const t = tables[idx];
        const visibleRows = Math.min(t.columns.length, MAX_ROWS) + (t.columns.length > MAX_ROWS ? 1 : 0);
        const h = HEADER_H + visibleRows * ROW_H + 10;
        if (h > maxRowH) maxRowH = h;
        positions.set(tableKey(t), {
          x: c * (CARD_W + GAP_X),
          y: rowY,
          h,
          table: t,
        });
      }
      rowY += maxRowH + GAP_Y;
    }

    const edges: { id: string; fromX: number; fromY: number; toX: number; toY: number; label: string }[] = [];
    for (const [fromKey, pos] of positions) {
      for (const fk of pos.table.foreignKeys) {
        const target = Array.from(positions.values()).find(
          (p) =>
            (p.table.tableName.toLowerCase() === fk.principalTable.toLowerCase() &&
              (p.table.schema ?? "").toLowerCase() === (fk.principalSchema ?? "").toLowerCase()) ||
            p.table.entityName.toLowerCase() === fk.principalEntityName.toLowerCase()
        );
        if (!target || tableKey(target.table) === fromKey) continue;

        const fromRight = pos.x < target.x;
        const fromX = fromRight ? pos.x + CARD_W : pos.x;
        const toX = fromRight ? target.x : target.x + CARD_W;
        const fromY = pos.y + HEADER_H + 14;
        const toY = target.y + HEADER_H + 14;
        edges.push({
          id: `${fromKey}->${tableKey(target.table)}::${fk.name}`,
          fromX,
          fromY,
          toX,
          toY,
          label: fk.name,
        });
      }
    }

    return { positions, edges };
  }, [tables]);

  return (
    <div className="flex flex-col h-[calc(100vh-3.5rem)]">
      {/* Toolbar */}
      <div className="px-4 py-2.5 border-b border-slate-200 dark:border-slate-800 bg-slate-50/70 dark:bg-slate-900/60 flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-1.5">
          <button
            onClick={() => setActiveRegistry("ALL")}
            className={`px-2.5 py-1 text-xs rounded-full border transition ${
              activeRegistry === "ALL"
                ? "bg-sky-500 text-white border-sky-500 font-medium"
                : "border-slate-300 dark:border-slate-700 text-slate-600 dark:text-slate-300 hover:bg-slate-200/60 dark:hover:bg-slate-800"
            }`}
          >
            All Registries ({model.registries.reduce((a, r) => a + r.tables.length, 0)})
          </button>
          {model.registries.map((r) => (
            <button
              key={r.name}
              onClick={() => setActiveRegistry(r.name)}
              className={`px-2.5 py-1 text-xs rounded-full border transition flex items-center gap-1.5 ${
                activeRegistry === r.name
                  ? "bg-slate-900 dark:bg-slate-100 text-white dark:text-slate-950 border-transparent font-medium"
                  : "border-slate-300 dark:border-slate-700 text-slate-600 dark:text-slate-300 hover:bg-slate-200/60 dark:hover:bg-slate-800"
              }`}
            >
              <span
                className="w-2 h-2 rounded-full"
                style={{ backgroundColor: registryColorMap.get(r.name) }}
              />
              {r.name} ({r.tables.length})
            </button>
          ))}
        </div>

        <div className="flex items-center gap-1.5">
          <button
            onClick={() => setZoom((z) => Math.max(0.35, +(z - 0.15).toFixed(2)))}
            className="p-1.5 rounded-lg border border-slate-200 dark:border-slate-700 hover:bg-slate-200/60 dark:hover:bg-slate-800"
            title="Zoom out"
          >
            <ZoomOut className="w-4 h-4" />
          </button>
          <span className="text-xs font-mono w-12 text-center">{Math.round(zoom * 100)}%</span>
          <button
            onClick={() => setZoom((z) => Math.min(2, +(z + 0.15).toFixed(2)))}
            className="p-1.5 rounded-lg border border-slate-200 dark:border-slate-700 hover:bg-slate-200/60 dark:hover:bg-slate-800"
            title="Zoom in"
          >
            <ZoomIn className="w-4 h-4" />
          </button>
          <button
            onClick={() => {
              setZoom(1);
              setPan({ x: 40, y: 40 });
            }}
            className="p-1.5 rounded-lg border border-slate-200 dark:border-slate-700 hover:bg-slate-200/60 dark:hover:bg-slate-800"
            title="Reset view"
          >
            <RotateCcw className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Pan/Zoom SVG Canvas */}
      <div
        className="flex-1 overflow-hidden bg-slate-100 dark:bg-slate-950 relative cursor-grab active:cursor-grabbing select-none"
        onMouseDown={(e) =>
          setDragging({ startX: e.clientX, startY: e.clientY, origX: pan.x, origY: pan.y })
        }
        onMouseMove={(e) => {
          if (!dragging) return;
          setPan({
            x: dragging.origX + (e.clientX - dragging.startX),
            y: dragging.origY + (e.clientY - dragging.startY),
          });
        }}
        onMouseUp={() => setDragging(null)}
        onMouseLeave={() => setDragging(null)}
        onWheel={(e) => {
          e.preventDefault();
          const delta = e.deltaY < 0 ? 0.08 : -0.08;
          setZoom((z) => Math.min(2, Math.max(0.35, +(z + delta).toFixed(2))));
        }}
      >
        <svg className="w-full h-full">
          <defs>
            <marker id="fk-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" markerHeight="6" orient="auto">
              <path d="M 0 1 L 10 5 L 0 9 z" fill="#38bdf8" />
            </marker>
          </defs>

          <g transform={`translate(${pan.x}, ${pan.y}) scale(${zoom})`}>
            {/* FK Edges */}
            {layout.edges.map((edge) => {
              const midX = (edge.fromX + edge.toX) / 2;
              const d = `M ${edge.fromX} ${edge.fromY} C ${midX} ${edge.fromY}, ${midX} ${edge.toY}, ${edge.toX} ${edge.toY}`;
              return (
                <path
                  key={edge.id}
                  d={d}
                  fill="none"
                  stroke="#38bdf8"
                  strokeOpacity={0.6}
                  strokeWidth={1.6}
                  markerEnd="url(#fk-arrow)"
                >
                  <title>{edge.label}</title>
                </path>
              );
            })}

            {/* Table Cards */}
            {Array.from(layout.positions.entries()).map(([key, pos]) => {
              const color = registryColorMap.get(pos.table.registryName) ?? "#0ea5e9";
              const visibleCols = pos.table.columns.slice(0, MAX_ROWS);
              return (
                <g
                  key={key}
                  transform={`translate(${pos.x}, ${pos.y})`}
                  onClick={(e) => {
                    e.stopPropagation();
                    onInspectTable(key);
                  }}
                  className="cursor-pointer"
                >
                  <rect
                    width={CARD_W}
                    height={pos.h}
                    rx={10}
                    className="fill-white dark:fill-slate-900 stroke-slate-300 dark:stroke-slate-700"
                    strokeWidth={1.2}
                  />
                  <rect width={CARD_W} height={4} rx={2} fill={color} />
                  <text
                    x={12}
                    y={21}
                    className="fill-slate-900 dark:fill-slate-100 text-[12px] font-bold"
                  >
                    {pos.table.entityName}
                  </text>
                  <text
                    x={12}
                    y={36}
                    className="fill-slate-500 dark:fill-slate-400 text-[10px] font-mono"
                  >
                    {pos.table.qualifiedName}
                  </text>
                  <line
                    x1={0}
                    y1={HEADER_H}
                    x2={CARD_W}
                    y2={HEADER_H}
                    className="stroke-slate-200 dark:stroke-slate-800"
                  />

                  {visibleCols.map((col, idx) => {
                    const y = HEADER_H + (idx + 1) * ROW_H - 5;
                    return (
                      <g key={col.columnName}>
                        <text
                          x={12}
                          y={y}
                          className={`text-[10.5px] font-mono ${
                            col.isPrimaryKey
                              ? "fill-amber-600 dark:fill-amber-400 font-semibold"
                              : col.isForeignKey
                                ? "fill-sky-600 dark:fill-sky-400"
                                : "fill-slate-700 dark:fill-slate-300"
                          }`}
                        >
                          {col.isPrimaryKey ? "🔑 " : col.isForeignKey ? "→ " : ""}
                          {col.columnName.length > 20 ? `${col.columnName.slice(0, 19)}…` : col.columnName}
                        </text>
                        <text
                          x={CARD_W - 10}
                          y={y}
                          textAnchor="end"
                          className="fill-slate-400 text-[9.5px] font-mono"
                        >
                          {col.sqlType.length > 14 ? `${col.sqlType.slice(0, 13)}…` : col.sqlType}
                        </text>
                      </g>
                    );
                  })}

                  {pos.table.columns.length > MAX_ROWS && (
                    <text
                      x={12}
                      y={HEADER_H + (MAX_ROWS + 1) * ROW_H - 5}
                      className="fill-slate-400 text-[10px] italic"
                    >
                      +{pos.table.columns.length - MAX_ROWS} more columns (click to inspect)
                    </text>
                  )}
                </g>
              );
            })}
          </g>
        </svg>
      </div>
    </div>
  );
}
