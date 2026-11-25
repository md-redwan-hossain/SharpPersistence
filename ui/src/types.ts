export interface UiSnapshot {
  projectName: string;
  startupProjectPath: string;
  dialect: string;
  generatedAtUtc: string;
  model: UiModel;
  runlist: UiRunlist;
  doctor: UiDoctor;
}

export interface UiModel {
  registries: UiRegistry[];
  postgresEnums: UiPostgresEnum[];
  sequences: UiSequence[];
}

export interface UiRegistry {
  name: string;
  tables: UiTable[];
}

export interface UiTable {
  registryName: string;
  entityName: string;
  tableName: string;
  schema: string | null;
  qualifiedName: string;
  comment: string | null;
  excludeFromMigrations: boolean;
  isUnlogged: boolean;
  tablespace: string | null;
  storageParameters: Record<string, string> | null;
  primaryKey: UiPrimaryKey | null;
  columns: UiColumn[];
  foreignKeys: UiForeignKey[];
  indexes: UiIndex[];
  checkConstraints: UiCheckConstraint[];
  exclusionConstraints: UiExclusionConstraint[];
  ddlPreview: string;
}

export interface UiPrimaryKey {
  name: string;
  columns: string[];
}

export interface UiColumn {
  columnName: string;
  propertyName: string;
  clrTypeName: string;
  sqlType: string;
  isNullable: boolean;
  isPrimaryKey: boolean;
  isForeignKey: boolean;
  isIdentity: boolean;
  isOwnedColumn: boolean;
  isConcurrencyToken: boolean;
  isRowVersion: boolean;
  isSystemColumn: boolean;
  maxLength: number | null;
  precision: number | null;
  scale: number | null;
  defaultValueSql: string | null;
  computedColumnSql: string | null;
  isComputedStored: boolean;
  comment: string | null;
}

export interface UiForeignKey {
  name: string;
  dependentColumns: string[];
  principalEntityName: string;
  principalSchema: string | null;
  principalTable: string;
  principalQualifiedName: string;
  principalColumns: string[];
  onDelete: string;
  isCircular: boolean;
}

export interface UiIndex {
  name: string;
  columns: string[];
  isDescending: boolean[] | null;
  isUnique: boolean;
  filterSql: string | null;
  includedColumns: string[] | null;
  method: string | null;
  tablespace: string | null;
  storageParameters: Record<string, string> | null;
}

export interface UiCheckConstraint {
  name: string;
  sql: string;
}

export interface UiExclusionConstraint {
  name: string;
  method: string;
  elements: string[];
  filterSql: string | null;
}

export interface UiPostgresEnum {
  name: string;
  schema: string | null;
  labels: string[];
}

export interface UiSequence {
  name: string;
  schema: string | null;
  clrTypeName: string;
  startValue: number;
  incrementBy: number;
  minValue: number | null;
  maxValue: number | null;
  isCyclic: boolean;
}

export interface UiRunlist {
  error: string | null;
  scripts: UiRunlistScript[];
}

export interface UiRunlistScript {
  order: number;
  id: string;
  registryName: string;
  scriptName: string;
  isOverridden: boolean;
  sql: string | null;
}

export interface UiDoctor {
  checkedDatabase: boolean;
  passedCount: number;
  warningCount: number;
  errorCount: number;
  wiringChecks: UiDoctorItem[];
  registryReports: UiDoctorRegistry[];
  crossRegistryChecks: UiDoctorItem[];
  migrationCoverageChecks: UiDoctorItem[];
  databaseChecks: UiDoctorItem[];
}

export interface UiDoctorRegistry {
  registryName: string;
  entityCount: number;
  excludedEntityCount: number;
  columnCount: number;
  foreignKeyCount: number;
  constraintCount: number;
  items: UiDoctorItem[];
}

export interface UiDoctorItem {
  severity: "Pass" | "Warning" | "Error";
  message: string;
  hint: string | null;
  entityName: string | null;
}

declare global {
  interface Window {
    __SHARP_PERSISTENCE_SNAPSHOT__?: UiSnapshot | null;
  }
}
