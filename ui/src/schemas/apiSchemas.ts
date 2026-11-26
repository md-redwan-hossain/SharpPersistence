import { z } from "zod";

export const UiTableSummarySchema = z.object({
  registryName: z.string(),
  entityName: z.string(),
  shortEntityName: z.string(),
  tableName: z.string(),
  schema: z.string().nullable(),
  qualifiedName: z.string(),
  excludeFromMigrations: z.boolean(),
  columnCount: z.number(),
  foreignKeyCount: z.number(),
  indexCount: z.number(),
});

export const UiRegistrySummarySchema = z.object({
  name: z.string(),
  tables: z.array(UiTableSummarySchema),
});

export const UiPostgresEnumSchema = z.object({
  name: z.string(),
  schema: z.string().nullable(),
  labels: z.array(z.string()),
});

export const UiSequenceSchema = z.object({
  name: z.string(),
  schema: z.string().nullable(),
  clrTypeName: z.string(),
  startValue: z.number(),
  incrementBy: z.number(),
  minValue: z.number().nullable(),
  maxValue: z.number().nullable(),
  isCyclic: z.boolean(),
});

export const UiOverviewSchema = z.object({
  projectName: z.string(),
  startupProjectPath: z.string(),
  dialect: z.string(),
  totalEntities: z.number(),
  runlistScriptCount: z.number(),
  runlistError: z.string().nullable(),
  registries: z.array(UiRegistrySummarySchema),
  postgresEnums: z.array(UiPostgresEnumSchema),
  sequences: z.array(UiSequenceSchema),
});

export const UiPrimaryKeySchema = z.object({
  name: z.string(),
  columns: z.array(z.string()),
});

export const UiColumnSchema = z.object({
  columnName: z.string(),
  propertyName: z.string(),
  clrTypeName: z.string(),
  sqlType: z.string(),
  isNullable: z.boolean(),
  isPrimaryKey: z.boolean(),
  isForeignKey: z.boolean(),
  isIdentity: z.boolean(),
  isOwnedColumn: z.boolean(),
  isConcurrencyToken: z.boolean(),
  isRowVersion: z.boolean(),
  isSystemColumn: z.boolean(),
  maxLength: z.number().nullable(),
  precision: z.number().nullable(),
  scale: z.number().nullable(),
  defaultValueSql: z.string().nullable(),
  computedColumnSql: z.string().nullable(),
  isComputedStored: z.boolean(),
  comment: z.string().nullable(),
});

export const UiForeignKeySchema = z.object({
  name: z.string(),
  dependentColumns: z.array(z.string()),
  dependentProperties: z.array(z.string()),
  principalEntityName: z.string(),
  principalSchema: z.string().nullable(),
  principalTable: z.string(),
  principalQualifiedName: z.string(),
  principalColumns: z.array(z.string()),
  principalProperties: z.array(z.string()),
  onDelete: z.string(),
  isCircular: z.boolean(),
});

export const UiIndexSchema = z.object({
  name: z.string(),
  columns: z.array(z.string()),
  isDescending: z.array(z.boolean()).nullable(),
  isUnique: z.boolean(),
  filterSql: z.string().nullable(),
  includedColumns: z.array(z.string()).nullable(),
  includedProperties: z.array(z.string()).nullable(),
  operatorsByColumn: z.record(z.string(), z.string()).nullable(),
  method: z.string().nullable(),
  tablespace: z.string().nullable(),
  storageParameters: z.record(z.string(), z.string()).nullable(),
});

export const UiCheckConstraintSchema = z.object({
  name: z.string(),
  sql: z.string(),
});

export const UiExclusionConstraintSchema = z.object({
  name: z.string(),
  method: z.string(),
  elements: z.array(z.string()),
  filterSql: z.string().nullable(),
});

export const UiTableDetailSchema = z.object({
  registryName: z.string(),
  entityName: z.string(),
  shortEntityName: z.string(),
  tableName: z.string(),
  schema: z.string().nullable(),
  qualifiedName: z.string(),
  comment: z.string().nullable(),
  excludeFromMigrations: z.boolean(),
  isUnlogged: z.boolean(),
  tablespace: z.string().nullable(),
  storageParameters: z.record(z.string(), z.string()).nullable(),
  primaryKey: UiPrimaryKeySchema.nullable(),
  columns: z.array(UiColumnSchema),
  foreignKeys: z.array(UiForeignKeySchema),
  indexes: z.array(UiIndexSchema),
  checkConstraints: z.array(UiCheckConstraintSchema),
  exclusionConstraints: z.array(UiExclusionConstraintSchema),
});

export const UiErdColumnSchema = z.object({
  columnName: z.string(),
  sqlType: z.string(),
  isPrimaryKey: z.boolean(),
  isForeignKey: z.boolean(),
  isNullable: z.boolean(),
});

export const UiErdNodeSchema = z.object({
  key: z.string(),
  registryName: z.string(),
  entityName: z.string(),
  shortEntityName: z.string(),
  qualifiedName: z.string(),
  excludeFromMigrations: z.boolean(),
  totalColumns: z.number(),
  columns: z.array(UiErdColumnSchema),
});

export const UiErdEdgeSchema = z.object({
  id: z.string(),
  fromKey: z.string(),
  toKey: z.string(),
  name: z.string(),
  onDelete: z.string(),
});

export const UiErdSchema = z.object({
  registryFilter: z.string().nullable(),
  nodes: z.array(UiErdNodeSchema),
  edges: z.array(UiErdEdgeSchema),
});

export const UiRunlistScriptSchema = z.object({
  order: z.number(),
  id: z.string(),
  registryName: z.string(),
  scriptName: z.string(),
  isOverridden: z.boolean(),
});

export const UiRunlistSchema = z.object({
  error: z.string().nullable(),
  scripts: z.array(UiRunlistScriptSchema),
});

export const UiDoctorItemSchema = z.object({
  severity: z.enum(["Pass", "Warning", "Error"]),
  message: z.string(),
  hint: z.string().nullable(),
  entityName: z.string().nullable(),
});

export const UiDoctorRegistrySchema = z.object({
  registryName: z.string(),
  entityCount: z.number(),
  excludedEntityCount: z.number(),
  columnCount: z.number(),
  foreignKeyCount: z.number(),
  constraintCount: z.number(),
  items: z.array(UiDoctorItemSchema),
});

export const UiDoctorSchema = z.object({
  checkedDatabase: z.boolean(),
  passedCount: z.number(),
  warningCount: z.number(),
  errorCount: z.number(),
  wiringChecks: z.array(UiDoctorItemSchema),
  registryReports: z.array(UiDoctorRegistrySchema),
  crossRegistryChecks: z.array(UiDoctorItemSchema),
  migrationCoverageChecks: z.array(UiDoctorItemSchema),
  databaseChecks: z.array(UiDoctorItemSchema),
});

export const UiDependencyNodeSchema = z.object({
  order: z.number(),
  registryName: z.string(),
  entityCount: z.number(),
  excludedCount: z.number(),
  schemas: z.array(z.string()),
  incomingCount: z.number(),
  outgoingCount: z.number(),
});

export const UiDependencyFkSchema = z.object({
  constraintName: z.string(),
  fromEntity: z.string(),
  fromTable: z.string(),
  fromColumns: z.array(z.string()),
  toEntity: z.string(),
  toTable: z.string(),
  toColumns: z.array(z.string()),
  onDelete: z.string(),
  isCircular: z.boolean(),
});

export const UiDependencyEdgeSchema = z.object({
  id: z.string(),
  fromRegistry: z.string(),
  toRegistry: z.string(),
  foreignKeys: z.array(UiDependencyFkSchema),
});

export const UiEntityStubSchema = z.object({
  stubEntity: z.string(),
  shortStubEntity: z.string(),
  targetEntity: z.string(),
  shortTargetEntity: z.string(),
  targetRegistry: z.string().nullable(),
  targetTable: z.string().nullable(),
});

export const UiDependenciesSchema = z.object({
  nodes: z.array(UiDependencyNodeSchema),
  edges: z.array(UiDependencyEdgeSchema),
  stubs: z.array(UiEntityStubSchema),
});

export const UiDryRunOperationSchema = z.object({
  kind: z.enum(["Add", "Alter", "Drop"]),
  objectType: z.string(),
  target: z.string(),
  summary: z.string(),
});

export const UiDryRunRegistrySchema = z.object({
  registryName: z.string(),
  hasChanges: z.boolean(),
  hasDestructiveChanges: z.boolean(),
  addCount: z.number(),
  alterCount: z.number(),
  dropCount: z.number(),
  operations: z.array(UiDryRunOperationSchema),
  sqlScript: z.string(),
  deferredSqlScript: z.string(),
});

export const UiDryRunSchema = z.object({
  changedRegistryCount: z.number(),
  addCount: z.number(),
  alterCount: z.number(),
  dropCount: z.number(),
  registries: z.array(UiDryRunRegistrySchema),
});

export const UiSearchHitSchema = z.object({
  kind: z.enum(["Column", "Index", "ForeignKey", "CheckConstraint", "ExclusionConstraint"]),
  name: z.string(),
  detail: z.string(),
  registryName: z.string(),
  entityName: z.string(),
  shortEntityName: z.string(),
  qualifiedName: z.string(),
});

export const UiSearchResponseSchema = z.object({
  query: z.string(),
  hits: z.array(UiSearchHitSchema),
});

export type UiOverview = z.infer<typeof UiOverviewSchema>;
export type UiRegistrySummary = z.infer<typeof UiRegistrySummarySchema>;
export type UiTableSummary = z.infer<typeof UiTableSummarySchema>;
export type UiTableDetail = z.infer<typeof UiTableDetailSchema>;
export type UiErd = z.infer<typeof UiErdSchema>;
export type UiErdNode = z.infer<typeof UiErdNodeSchema>;
export type UiErdEdge = z.infer<typeof UiErdEdgeSchema>;
export type UiRunlist = z.infer<typeof UiRunlistSchema>;
export type UiRunlistScript = z.infer<typeof UiRunlistScriptSchema>;
export type UiDoctor = z.infer<typeof UiDoctorSchema>;
export type UiDoctorItem = z.infer<typeof UiDoctorItemSchema>;
export type UiDependencies = z.infer<typeof UiDependenciesSchema>;
export type UiDependencyNode = z.infer<typeof UiDependencyNodeSchema>;
export type UiDependencyEdge = z.infer<typeof UiDependencyEdgeSchema>;
export type UiDryRun = z.infer<typeof UiDryRunSchema>;
export type UiDryRunRegistry = z.infer<typeof UiDryRunRegistrySchema>;
export type UiSearchHit = z.infer<typeof UiSearchHitSchema>;
export type UiSearchResponse = z.infer<typeof UiSearchResponseSchema>;

export async function fetchValidatedJson<T>(
  url: string,
  schema: z.ZodType<T>,
  signal?: AbortSignal
): Promise<T> {
  const res = await fetch(url, { signal });
  if (!res.ok) {
    throw new Error(`Request to ${url} failed with HTTP ${res.status}`);
  }

  const raw: unknown = await res.json();
  const parsed = schema.safeParse(raw);
  if (!parsed.success) {
    const firstIssues = parsed.error.issues
      .slice(0, 3)
      .map((i) => `${i.path.join(".") || "(root)"}: ${i.message}`)
      .join("; ");
    throw new Error(`API schema validation failed for ${url}: ${firstIssues}`);
  }

  return parsed.data;
}


export const UiDdlStatementSchema = z.object({
  index: z.number(),
  action: z.string(),
  objectType: z.string(),
  schema: z.string().nullable(),
  tableName: z.string().nullable(),
  objectName: z.string(),
  target: z.string(),
  sql: z.string(),
});

export const UiScriptDdlsSchema = z.object({
  scriptId: z.string(),
  statements: z.array(UiDdlStatementSchema),
});

export const UiDdlCheckResultSchema = z.object({
  index: z.number().nullable(),
  target: z.string(),
  passed: z.boolean(),
  existsInDb: z.boolean(),
  detail: z.string(),
  error: z.string().nullable(),
});

export const UiScriptLiveCheckSchema = z.object({
  scriptId: z.string(),
  connected: z.boolean(),
  isAppliedInHistory: z.boolean().nullable(),
  elapsedMs: z.number(),
  error: z.string().nullable(),
  results: z.array(UiDdlCheckResultSchema),
});

export type UiDdlStatement = z.infer<typeof UiDdlStatementSchema>;
export type UiScriptDdls = z.infer<typeof UiScriptDdlsSchema>;
export type UiDdlCheckResult = z.infer<typeof UiDdlCheckResultSchema>;
export type UiScriptLiveCheck = z.infer<typeof UiScriptLiveCheckSchema>;

export async function postValidatedJson<T>(
  url: string,
  body: unknown,
  schema: z.ZodType<T>,
  signal?: AbortSignal
): Promise<T> {
  const res = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
    signal,
  });
  if (!res.ok) {
    throw new Error(`Request to ${url} failed with HTTP ${res.status}`);
  }
  const raw: unknown = await res.json();
  const parsed = schema.safeParse(raw);
  if (!parsed.success) {
    throw new Error(`API schema validation failed for ${url}`);
  }
  return parsed.data;
}
