; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SPSQL001 | SharpPersistence.SqlParser | Error | SQL start tag is missing a tag name or matching end tag
SPSQL002 | SharpPersistence.SqlParser | Error | SQL end tag is missing a tag name, has no matching start tag, or is duplicated
SPSQL003 | SharpPersistence.SqlParser | Error | SQL block between start and end tags is empty
SPSQL004 | SharpPersistence.SqlParser | Error | Duplicate SQL tag found within a file or across SQL files
SPSQL005 | SharpPersistence.SqlParser | Error | Multiple SQL tags map to the same generated C# extension method name
