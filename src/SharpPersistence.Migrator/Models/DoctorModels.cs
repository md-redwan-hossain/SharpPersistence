using SharpPersistence.Enums;

namespace SharpPersistence.Migrator.Models;

internal enum DoctorSeverity
{
    Pass = 0,
    Warning = 1,
    Error = 2
}

internal sealed record DoctorCheckItem(
    DoctorSeverity Severity,
    string Message,
    string? Hint = null,
    string? EntityName = null);

internal sealed record DoctorRegistryReport(
    string RegistryName,
    int EntityCount,
    int ColumnCount,
    int ForeignKeyCount,
    int ConstraintCount,
    IReadOnlyList<DoctorCheckItem> Items)
{
    public bool HasErrors => Items.Any(i => i.Severity == DoctorSeverity.Error);
    public bool HasWarnings => Items.Any(i => i.Severity == DoctorSeverity.Warning);
}

internal sealed record DoctorReport(
    SqlDialect Dialect,
    bool CheckedDatabase,
    IReadOnlyList<DoctorCheckItem> WiringChecks,
    IReadOnlyList<DoctorRegistryReport> RegistryReports,
    IReadOnlyList<DoctorCheckItem> CrossRegistryChecks,
    IReadOnlyList<DoctorCheckItem> MigrationCoverageChecks,
    IReadOnlyList<DoctorCheckItem> DatabaseChecks)
{
    private IEnumerable<DoctorCheckItem> AllItems =>
        WiringChecks
            .Concat(RegistryReports.SelectMany(r => r.Items))
            .Concat(CrossRegistryChecks)
            .Concat(MigrationCoverageChecks)
            .Concat(DatabaseChecks);

    public int PassedCount => AllItems.Count(i => i.Severity == DoctorSeverity.Pass);
    public int WarningCount => AllItems.Count(i => i.Severity == DoctorSeverity.Warning);
    public int ErrorCount => AllItems.Count(i => i.Severity == DoctorSeverity.Error);
    public bool HasErrors => ErrorCount > 0;
}
