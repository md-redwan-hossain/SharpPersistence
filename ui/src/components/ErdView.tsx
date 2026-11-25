import { useEffect, useMemo, useState } from "react";
import { Loader2, RotateCcw, ZoomIn, ZoomOut } from "lucide-react";
import { API_ROUTES } from "../lib/apiRoutes";
import {
  fetchValidatedJson,
  UiErdSchema,
  type UiErd,
  type UiErdNode,
  type UiOverview,
} from "../schemas/apiSchemas";
import { Button } from "./ui/button";

interface Props {
  overview: UiOverview;
  initialRegistry?: string | null;
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

const CARD_W = 244;
const HEADER_H = 44;
const ROW_H = 22;
const MAX_ROWS = 10;
const GAP_X = 88;
const GAP_Y = 64;

export function ErdView({ overview, initialRegistry, onInspectTable }: Props) {
  const defaultFilter =
    initialRegistry ??
    (overview.totalEntities > 40 && overview.registries.length > 1
      ? overview.registries[0].name
      : "ALL");

  const [activeRegistry, setActiveRegistry] = useState<string>(defaultFilter);

  useEffect(() => {
    if (initialRegistry) {
      setActiveRegistry(initialRegistry);
    }
  }, [initialRegistry]);
  const [erdCache, setErdCache] = useState<Record<string, UiErd>>({});
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 36, y: 36 });
  const [dragging, setDragging] = useState<{ startX: number; startY: number; origX: number; origY: number } | null>(null);

  const registryColorMap = useMemo(() => {
    const map = new Map<string, string>();
    overview.registries.forEach((r, idx) => {
      map.set(r.name, REGISTRY_COLORS[idx % REGISTRY_COLORS.length]);
    });
    return map;
  }, [overview.registries]);

  useEffect(() => {
    if (erdCache[activeRegistry]) {
      setError(null);
      return;
    }

    const controller = new AbortController();
    setLoading(true);
    setError(null);
    const url = API_ROUTES.erd(activeRegistry);

    fetchValidatedJson(url, UiErdSchema, controller.signal)
      .then((data) => setErdCache((prev) => ({ ...prev, [activeRegistry]: data })))
      .catch((err: unknown) => {
        if (controller.signal.aborted) return;
        setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });

    return () => controller.abort();
  }, [activeRegistry, erdCache]);

  const erd = erdCache[activeRegistry] ?? null;

  const layout = useMemo(() => {
    const positions = new Map<string, { x: number; y: number; h: number; node: UiErdNode }>();
    if (!erd) return { positions, edges: [] };

    const cols = Math.max(2, Math.ceil(Math.sqrt(erd.nodes.length)));
    let rowY = 0;

    for (let r = 0; r < Math.ceil(erd.nodes.length / cols); r++) {
      let maxRowH = 0;
      for (let c = 0; c < cols; c++) {
        const idx = r * cols + c;
        if (idx >= erd.nodes.length) break;
        const node = erd.nodes[idx];
        const visibleRows =
          Math.min(node.columns.length, MAX_ROWS) + (node.totalColumns > MAX_ROWS ? 1 : 0);
        const h = HEADER_H + visibleRows * ROW_H + 10;
        if (h > maxRowH) maxRowH = h;
        positions.set(node.key, { x: c * (CARD_W + GAP_X), y: rowY, h, node });
      }
      rowY += maxRowH + GAP_Y;
    }

    const edges = erd.edges
      .map((edge) => {
        const from = positions.get(edge.fromKey);
        const to = positions.get(edge.toKey);
        if (!from || !to) return null;
        const fromRight = from.x < to.x;
        return {
          ...edge,
          fromX: fromRight ? from.x + CARD_W : from.x,
          fromY: from.y + HEADER_H + 14,
          toX: fromRight ? to.x : to.x + CARD_W,
          toY: to.y + HEADER_H + 14,
        };
      })
      .filter((e): e is NonNullable<typeof e> => e !== null);

    return { positions, edges };
  }, [erd]);

  return (
    <div className="flex flex-col h-[calc(100vh-6.5rem)] lg:h-[calc(100vh-3.5rem)]">
      {/* Toolbar */}
      <div className="px-3 sm:px-4 py-2 border-b border-border bg-muted/30 flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-1.5">
          <Button
            size="sm"
            variant={activeRegistry === "ALL" ? "default" : "outline"}
            onClick={() => setActiveRegistry("ALL")}
          >
            All ({overview.totalEntities})
          </Button>
          {overview.registries.map((r) => (
            <Button
              key={r.name}
              size="sm"
              variant={activeRegistry === r.name ? "default" : "outline"}
              onClick={() => setActiveRegistry(r.name)}
            >
              <span
                className="w-2 h-2 rounded-full"
                style={{ backgroundColor: registryColorMap.get(r.name) }}
              />
              {r.name} ({r.tables.length})
            </Button>
          ))}
        </div>

        <div className="flex items-center gap-1">
          <Button
            size="icon"
            variant="outline"
            onClick={() => setZoom((z) => Math.max(0.35, +(z - 0.15).toFixed(2)))}
            title="Zoom out"
          >
            <ZoomOut className="w-3.5 h-3.5" />
          </Button>
          <span className="text-xs font-mono tabular-nums w-11 text-center">{Math.round(zoom * 100)}%</span>
          <Button
            size="icon"
            variant="outline"
            onClick={() => setZoom((z) => Math.min(2, +(z + 0.15).toFixed(2)))}
            title="Zoom in"
          >
            <ZoomIn className="w-3.5 h-3.5" />
          </Button>
          <Button
            size="icon"
            variant="outline"
            onClick={() => {
              setZoom(1);
              setPan({ x: 36, y: 36 });
            }}
            title="Reset view"
          >
            <RotateCcw className="w-3.5 h-3.5" />
          </Button>
        </div>
      </div>

      {/* Pan/Zoom SVG Canvas */}
      <div
        className="flex-1 overflow-hidden bg-muted/20 relative cursor-grab active:cursor-grabbing select-none"
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
        onTouchStart={(e) => {
          if (e.touches.length !== 1) return;
          const t = e.touches[0];
          setDragging({ startX: t.clientX, startY: t.clientY, origX: pan.x, origY: pan.y });
        }}
        onTouchMove={(e) => {
          if (!dragging || e.touches.length !== 1) return;
          const t = e.touches[0];
          setPan({
            x: dragging.origX + (t.clientX - dragging.startX),
            y: dragging.origY + (t.clientY - dragging.startY),
          });
        }}
        onTouchEnd={() => setDragging(null)}
        onWheel={(e) => {
          e.preventDefault();
          const delta = e.deltaY < 0 ? 0.08 : -0.08;
          setZoom((z) => Math.min(2, Math.max(0.35, +(z + delta).toFixed(2))));
        }}
      >
        {error ? (
          <div className="p-6 text-xs text-rose-500">{error}</div>
        ) : loading && !erd ? (
          <div className="h-full flex items-center justify-center gap-2 text-xs text-muted-foreground">
            <Loader2 className="w-4 h-4 animate-spin" />
            Loading ERD...
          </div>
        ) : (
          <svg className="w-full h-full">
            <defs>
              <marker id="fk-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" markerHeight="6" orient="auto">
                <path d="M 0 1 L 10 5 L 0 9 z" fill="#38bdf8" />
              </marker>
            </defs>

            <g transform={`translate(${pan.x}, ${pan.y}) scale(${zoom})`}>
              {layout.edges.map((edge) => {
                const midX = (edge.fromX + edge.toX) / 2;
                const d = `M ${edge.fromX} ${edge.fromY} C ${midX} ${edge.fromY}, ${midX} ${edge.toY}, ${edge.toX} ${edge.toY}`;
                return (
                  <path
                    key={edge.id}
                    d={d}
                    fill="none"
                    stroke="#38bdf8"
                    strokeOpacity={0.65}
                    strokeWidth={1.5}
                    markerEnd="url(#fk-arrow)"
                  >
                    <title>{`${edge.name} (ON DELETE ${edge.onDelete})`}</title>
                  </path>
                );
              })}

              {Array.from(layout.positions.entries()).map(([key, pos]) => {
                const color = registryColorMap.get(pos.node.registryName) ?? "#0ea5e9";
                const visibleCols = pos.node.columns.slice(0, MAX_ROWS);
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
                      className="fill-card stroke-border"
                      strokeWidth={1.2}
                    />
                    <rect width={CARD_W} height={4} rx={2} fill={color} />
                    <text x={12} y={21} className="fill-foreground text-[12px] font-bold">
                      {pos.node.shortEntityName}
                    </text>
                    <text x={12} y={36} className="fill-muted-foreground text-[10px] font-mono">
                      {pos.node.qualifiedName}
                    </text>
                    <line x1={0} y1={HEADER_H} x2={CARD_W} y2={HEADER_H} className="stroke-border" />

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
                                  : "fill-foreground"
                            }`}
                          >
                            {col.isPrimaryKey ? "PK " : col.isForeignKey ? "FK " : ""}
                            {col.columnName.length > 20 ? `${col.columnName.slice(0, 19)}…` : col.columnName}
                          </text>
                          <text x={CARD_W - 10} y={y} textAnchor="end" className="fill-muted-foreground text-[9.5px] font-mono">
                            {col.sqlType.length > 14 ? `${col.sqlType.slice(0, 13)}…` : col.sqlType}
                          </text>
                        </g>
                      );
                    })}

                    {pos.node.totalColumns > MAX_ROWS && (
                      <text x={12} y={HEADER_H + (MAX_ROWS + 1) * ROW_H - 5} className="fill-muted-foreground text-[10px] italic">
                        +{pos.node.totalColumns - MAX_ROWS} more columns
                      </text>
                    )}
                  </g>
                );
              })}
            </g>
          </svg>
        )}
      </div>
    </div>
  );
}
