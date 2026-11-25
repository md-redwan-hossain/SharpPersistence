export const API_ROUTES = {
  overview: "/api/overview",
  dependencies: "/api/dependencies",
  search: (query: string) => `/api/search?q=${encodeURIComponent(query)}&limit=40`,
  doctor: (checkDb: boolean) => `/api/doctor?checkDb=${checkDb}`,
  dryRun: "/api/migrations/dry-run",
  tableDetail: (registryName: string, entityName: string) =>
    `/api/tables/${encodeURIComponent(registryName)}/${encodeURIComponent(entityName)}`,
  erd: (registry: string) =>
    registry === "ALL" ? "/api/erd" : `/api/erd?registry=${encodeURIComponent(registry)}`,
  runlist: "/api/runlist",
  runlistSql: (scriptId: string) => `/api/runlist/${encodeURIComponent(scriptId)}/sql`,
} as const;
