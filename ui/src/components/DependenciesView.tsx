import { useEffect, useMemo, useState } from "react";
import {
  ArrowRight,
  GitFork,
  Layers,
  Link2,
  Loader2,
  Network,
  RotateCcw,
  Table2,
  ZoomIn,
  ZoomOut,
} from "lucide-react";
import {
  fetchValidatedJson,
  UiDependenciesSchema,
  type UiDependencies,
  type UiDependencyEdge,
  type UiDependencyNode,
} from "../schemas/apiSchemas";
import { Badge } from "./ui/badge";
import { Button } from "./ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "./ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "./ui/table";

interface Props {
  dependencies: UiDependencies | null;
  onLoaded: (data: UiDependencies) => void;
  onOpenInErd: (registryName: string) => void;
  onOpenInEntities: (registryName: string) => void;
}

const NODE_W = 240;
const NODE_H = 104;
const COL_GAP = 110;
const ROW_GAP = 36;

export function DependenciesView({ dependencies, onLoaded, onOpenInErd, onOpenInEntities }: Props) {
  const [loading, setLoading] = useState(dependencies === null);
  const [error, setError] = useState<string | null>(null);
  const [selectedNode, setSelectedNode] = useState<string | null>(null);
  const [selectedEdgeId, setSelectedEdgeId] = useState<string | null>(null);
  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 32, y: 32 });
  const [dragging, setDragging] = useState<{ startX: number; startY: number; origX: number; origY: number } | null>(null);

  useEffect(() => {
    if (dependencies !== null) return;
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    fetchValidatedJson("/api/dependencies", UiDependenciesSchema, controller.signal)
      .then(onLoaded)
      .catch((err: unknown) => {
        if (controller.signal.aborted) return;
        setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [dependencies, onLoaded]);

  const layout = useMemo(() => {
    if (!dependencies) return { positions: new Map<string, { x: number; y: number; node: UiDependencyNode }>(), width: 800, height: 320 };

    const depthByReg = new Map<string, number>();
    for (const n of dependencies.nodes) {
      depthByReg.set(n.registryName, 0);
    }

    // Relax depths: if A depends on B (edge A -> B), A sits to the right of B
    for (let iter = 0; iter < dependencies.nodes.length; iter++) {
      let changed = false;
      for (const edge of dependencies.edges) {
        const toDepth = depthByReg.get(edge.toRegistry) ?? 0;
        const fromDepth = depthByReg.get(edge.fromRegistry) ?? 0;
        if (fromDepth <= toDepth) {
          depthByReg.set(edge.fromRegistry, toDepth + 1);
          changed = true;
        }
      }
      if (!changed) break;
    }

    const columns = new Map<number, UiDependencyNode[]>();
    for (const node of dependencies.nodes) {
      const col = depthByReg.get(node.registryName) ?? 0;
      const list = columns.get(col) ?? [];
      list.push(node);
      columns.set(col, list);
    }

    const sortedCols = [...columns.keys()].sort((a, b) => a - b);
    const positions = new Map<string, { x: number; y: number; node: UiDependencyNode }>();
    let maxRows = 1;

    sortedCols.forEach((colKey, colIdx) => {
      const colNodes = columns.get(colKey) ?? [];
      maxRows = Math.max(maxRows, colNodes.length);
      colNodes.forEach((node, rowIdx) => {
        positions.set(node.registryName, {
          x: colIdx * (NODE_W + COL_GAP),
          y: rowIdx * (NODE_H + ROW_GAP),
          node,
        });
      });
    });

    const width = Math.max(720, sortedCols.length * (NODE_W + COL_GAP) + 80);
    const height = Math.max(280, maxRows * (NODE_H + ROW_GAP) + 80);
    return { positions, width, height };
  }, [dependencies]);

  const activeEdge: UiDependencyEdge | null = useMemo(() => {
    if (!dependencies || !selectedEdgeId) return null;
    return dependencies.edges.find((e) => e.id === selectedEdgeId) ?? null;
  }, [dependencies, selectedEdgeId]);

  const activeNodeObj: UiDependencyNode | null = useMemo(() => {
    if (!dependencies || !selectedNode) return null;
    return dependencies.nodes.find((n) => n.registryName === selectedNode) ?? null;
  }, [dependencies, selectedNode]);

  if (loading && !dependencies) {
    return (
      <div className="h-64 flex items-center justify-center gap-2 text-xs text-muted-foreground">
        <Loader2 className="w-4 h-4 animate-spin" />
        Loading registry dependency graph...
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-4 sm:p-6 max-w-3xl mx-auto">
        <Card className="border-rose-500/40">
          <CardContent className="text-xs text-rose-600 dark:text-rose-400">{error}</CardContent>
        </Card>
      </div>
    );
  }

  if (!dependencies) return null;

  return (
    <div className="lg:h-[calc(100vh-3.5rem)] overflow-y-auto p-3 sm:p-6">
      <div className="max-w-6xl mx-auto space-y-5">
        {/* Summary Bar */}
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
            <Badge variant="secondary">{dependencies.nodes.length} registries</Badge>
            <Badge variant="info">{dependencies.edges.length} cross-registry links</Badge>
            <Badge variant="violet">{dependencies.stubs.length} entity stubs</Badge>
            <span className="hidden sm:inline">
              Left-to-right topological dependency order. Click any registry or edge to inspect.
            </span>
          </div>

          <div className="flex items-center gap-1">
            <Button variant="outline" size="icon" onClick={() => setZoom((z) => Math.max(0.4, +(z - 0.15).toFixed(2)))} title="Zoom out">
              <ZoomOut className="w-3.5 h-3.5" />
            </Button>
            <span className="text-[11px] font-mono tabular-nums w-11 text-center text-muted-foreground">
              {Math.round(zoom * 100)}%
            </span>
            <Button variant="outline" size="icon" onClick={() => setZoom((z) => Math.min(2, +(z + 0.15).toFixed(2)))} title="Zoom in">
              <ZoomIn className="w-3.5 h-3.5" />
            </Button>
            <Button
              variant="outline"
              size="icon"
              onClick={() => {
                setZoom(1);
                setPan({ x: 32, y: 32 });
              }}
              title="Reset view"
            >
              <RotateCcw className="w-3.5 h-3.5" />
            </Button>
          </div>
        </div>

        {/* Interactive Graph Canvas */}
        <Card className="overflow-hidden">
          <CardHeader>
            <CardTitle>
              <Network className="w-4 h-4 text-muted-foreground" />
              Registry Dependency Graph
            </CardTitle>
            {dependencies.edges.length === 0 && (
              <span className="text-xs text-muted-foreground">No cross-registry foreign keys detected.</span>
            )}
          </CardHeader>
          <div
            className="relative h-[360px] sm:h-[420px] bg-muted/20 overflow-hidden select-none cursor-grab active:cursor-grabbing touch-none"
            onPointerDown={(e) => {
              if ((e.target as HTMLElement).closest("[data-interactive='true']")) return;
              setDragging({ startX: e.clientX, startY: e.clientY, origX: pan.x, origY: pan.y });
            }}
            onPointerMove={(e) => {
              if (!dragging) return;
              setPan({
                x: dragging.origX + (e.clientX - dragging.startX),
                y: dragging.origY + (e.clientY - dragging.startY),
              });
            }}
            onPointerUp={() => setDragging(null)}
            onPointerLeave={() => setDragging(null)}
            onWheel={(e) => {
              e.preventDefault();
              const delta = e.deltaY < 0 ? 0.1 : -0.1;
              setZoom((z) => Math.min(2, Math.max(0.4, +(z + delta).toFixed(2))));
            }}
          >
            <svg width="100%" height="100%">
              <defs>
                <marker id="dep-arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
                  <path d="M 0 1 L 10 5 L 0 9 z" className="fill-muted-foreground/70" />
                </marker>
                <marker id="dep-arrow-active" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
                  <path d="M 0 1 L 10 5 L 0 9 z" className="fill-sky-500" />
                </marker>
              </defs>

              <g transform={`translate(${pan.x}, ${pan.y}) scale(${zoom})`}>
                {/* Edges */}
                {dependencies.edges.map((edge) => {
                  const from = layout.positions.get(edge.fromRegistry);
                  const to = layout.positions.get(edge.toRegistry);
                  if (!from || !to) return null;

                  const isHighlighted =
                    selectedEdgeId === edge.id ||
                    selectedNode === edge.fromRegistry ||
                    selectedNode === edge.toRegistry;
                  const isDimmed = (selectedNode !== null || selectedEdgeId !== null) && !isHighlighted;

                  const startX = from.x;
                  const startY = from.y + NODE_H / 2;
                  const endX = to.x + NODE_W;
                  const endY = to.y + NODE_H / 2;
                  const midX = (startX + endX) / 2;
                  const midY = (startY + endY) / 2;
                  const pathD = `M ${startX} ${startY} C ${midX} ${startY}, ${midX} ${endY}, ${endX} ${endY}`;

                  return (
                    <g
                      key={edge.id}
                      data-interactive="true"
                      className="cursor-pointer"
                      opacity={isDimmed ? 0.25 : 1}
                      onClick={() => {
                        setSelectedEdgeId(edge.id);
                        setSelectedNode(null);
                      }}
                    >
                      <path d={pathD} fill="none" stroke="transparent" strokeWidth={14} />
                      <path
                        d={pathD}
                        fill="none"
                        strokeWidth={isHighlighted ? 2.5 : 1.75}
                        className={isHighlighted ? "stroke-sky-500" : "stroke-muted-foreground/50"}
                        markerEnd={isHighlighted ? "url(#dep-arrow-active)" : "url(#dep-arrow)"}
                      />
                      <g transform={`translate(${midX}, ${midY})`}>
                        <rect
                          x={-28}
                          y={-11}
                          width={56}
                          height={22}
                          rx={11}
                          className={
                            isHighlighted
                              ? "fill-sky-500/15 stroke-sky-500"
                              : "fill-card stroke-border"
                          }
                          strokeWidth={1}
                        />
                        <text
                          textAnchor="middle"
                          dominantBaseline="central"
                          className={`text-[10px] font-mono font-semibold ${
                            isHighlighted ? "fill-sky-600 dark:fill-sky-400" : "fill-muted-foreground"
                          }`}
                        >
                          {edge.foreignKeys.length} FK{edge.foreignKeys.length === 1 ? "" : "s"}
                        </text>
                      </g>
                    </g>
                  );
                })}

                {/* Nodes */}
                {[...layout.positions.values()].map(({ x, y, node }) => {
                  const isSelected = selectedNode === node.registryName;
                  const isConnectedToSelection =
                    selectedNode !== null &&
                    dependencies.edges.some(
                      (e) =>
                        (e.fromRegistry === selectedNode && e.toRegistry === node.registryName) ||
                        (e.toRegistry === selectedNode && e.fromRegistry === node.registryName)
                    );
                  const isConnectedToEdge =
                    activeEdge !== null &&
                    (activeEdge.fromRegistry === node.registryName || activeEdge.toRegistry === node.registryName);
                  const isDimmed =
                    (selectedNode !== null && !isSelected && !isConnectedToSelection) ||
                    (activeEdge !== null && !isConnectedToEdge);

                  return (
                    <g
                      key={node.registryName}
                      data-interactive="true"
                      transform={`translate(${x}, ${y})`}
                      opacity={isDimmed ? 0.35 : 1}
                      className="cursor-pointer"
                      onClick={() => {
                        setSelectedNode(node.registryName);
                        setSelectedEdgeId(null);
                      }}
                    >
                      <rect
                        width={NODE_W}
                        height={NODE_H}
                        rx={12}
                        className={`fill-card ${
                          isSelected ? "stroke-sky-500" : "stroke-border hover:stroke-foreground/40"
                        }`}
                        strokeWidth={isSelected ? 2 : 1.25}
                      />
                      <foreignObject width={NODE_W} height={NODE_H}>
                        <div className="h-full p-3 flex flex-col justify-between">
                          <div className="flex items-center justify-between gap-2">
                            <span className="text-xs font-semibold text-foreground truncate" title={node.registryName}>
                              {node.registryName}
                            </span>
                            <span className="px-1.5 py-0.5 text-[10px] font-mono rounded bg-muted text-muted-foreground shrink-0">
                              #{node.order}
                            </span>
                          </div>
                          <div className="flex flex-wrap items-center gap-1 text-[10px] font-mono text-muted-foreground">
                            <span>{node.entityCount} entities</span>
                            {node.excludedCount > 0 && <span>· {node.excludedCount} RO</span>}
                            <span>· {node.schemas.slice(0, 2).join(", ")}</span>
                          </div>
                          <div className="flex items-center justify-between text-[10px] font-mono text-muted-foreground border-t border-border/60 pt-1.5">
                            <span>In: {node.incomingCount} FKs</span>
                            <span>Out: {node.outgoingCount} FKs</span>
                          </div>
                        </div>
                      </foreignObject>
                    </g>
                  );
                })}
              </g>
            </svg>
          </div>
        </Card>

        {/* Selected Node Inspector */}
        {activeNodeObj && (
          <Card className="overflow-hidden">
            <CardHeader>
              <CardTitle>
                <Layers className="w-4 h-4 text-muted-foreground" />
                {activeNodeObj.registryName}
                <Badge variant="outline">Dependency Order #{activeNodeObj.order}</Badge>
              </CardTitle>
              <div className="flex items-center gap-2">
                <Button variant="outline" size="sm" onClick={() => onOpenInEntities(activeNodeObj.registryName)}>
                  <Table2 className="w-3.5 h-3.5" />
                  Entities
                </Button>
                <Button variant="outline" size="sm" onClick={() => onOpenInErd(activeNodeObj.registryName)}>
                  <GitFork className="w-3.5 h-3.5" />
                  Open in ERD
                </Button>
              </div>
            </CardHeader>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Entities</TableHead>
                  <TableHead>Excluded / ReadOnly</TableHead>
                  <TableHead>Schemas</TableHead>
                  <TableHead>Incoming Cross-Registry FKs</TableHead>
                  <TableHead>Outgoing Cross-Registry FKs</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow>
                  <TableCell className="font-mono font-semibold tabular-nums">
                    {activeNodeObj.entityCount}
                  </TableCell>
                  <TableCell className="font-mono tabular-nums">
                    {activeNodeObj.excludedCount}
                  </TableCell>
                  <TableCell className="font-mono">
                    <div className="flex flex-wrap gap-1">
                      {activeNodeObj.schemas.map((s) => (
                        <Badge key={s} variant="secondary" className="font-mono">
                          {s}
                        </Badge>
                      ))}
                    </div>
                  </TableCell>
                  <TableCell className="font-mono tabular-nums">
                    {activeNodeObj.incomingCount}
                  </TableCell>
                  <TableCell className="font-mono tabular-nums">
                    {activeNodeObj.outgoingCount}
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </Card>
        )}

        {/* Selected Edge FK Inspector */}
        {activeEdge && (
          <Card className="overflow-hidden">
            <CardHeader>
              <CardTitle>
                <Link2 className="w-4 h-4 text-muted-foreground" />
                <span>{activeEdge.fromRegistry}</span>
                <ArrowRight className="w-3.5 h-3.5 text-muted-foreground" />
                <span>{activeEdge.toRegistry}</span>
                <Badge variant="info">{activeEdge.foreignKeys.length} FKs</Badge>
              </CardTitle>
            </CardHeader>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Constraint</TableHead>
                  <TableHead>From Table</TableHead>
                  <TableHead>References</TableHead>
                  <TableHead>On Delete</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {activeEdge.foreignKeys.map((fk) => (
                  <TableRow key={`${fk.fromTable}-${fk.constraintName}`}>
                    <TableCell className="font-mono font-medium">
                      {fk.constraintName}
                      {fk.isCircular && <Badge variant="warning" className="ml-1.5">DEFERRED</Badge>}
                    </TableCell>
                    <TableCell className="font-mono">
                      {fk.fromTable}({fk.fromColumns.join(", ")})
                    </TableCell>
                    <TableCell className="font-mono text-sky-700 dark:text-sky-400">
                      {fk.toTable}({fk.toColumns.join(", ")})
                    </TableCell>
                    <TableCell>
                      <Badge variant="outline">{fk.onDelete.toUpperCase()}</Badge>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Card>
        )}

        {/* Entity Stub Mappings */}
        <Card className="overflow-hidden">
          <CardHeader>
            <CardTitle>
              <Layers className="w-4 h-4 text-muted-foreground" />
              Entity Stubs ({dependencies.stubs.length})
            </CardTitle>
          </CardHeader>
          {dependencies.stubs.length === 0 ? (
            <CardContent className="text-xs text-muted-foreground">
              No <code className="font-mono">ConfigureEntityStubs(...)</code> mappings configured.
            </CardContent>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Stub Type</TableHead>
                  <TableHead>Owning Entity</TableHead>
                  <TableHead>Owning Registry</TableHead>
                  <TableHead>Target Table</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {dependencies.stubs.map((s) => (
                  <TableRow key={s.stubEntity}>
                    <TableCell className="font-mono font-medium">
                      <div>{s.shortStubEntity}</div>
                      <div className="text-[11px] text-muted-foreground font-normal">{s.stubEntity}</div>
                    </TableCell>
                    <TableCell className="font-mono">
                      <div>{s.shortTargetEntity}</div>
                      <div className="text-[11px] text-muted-foreground font-normal">{s.targetEntity}</div>
                    </TableCell>
                    <TableCell className="font-mono">{s.targetRegistry ?? "—"}</TableCell>
                    <TableCell className="font-mono text-sky-700 dark:text-sky-400">
                      {s.targetTable ?? "—"}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </Card>
      </div>
    </div>
  );
}
