| Branch | Status                                                                                                                |
|--------|-----------------------------------------------------------------------------------------------------------------------|
| main   | ![Dotnet 9](https://github.com/md-redwan-hossain/SharpPersistence/actions/workflows/dotnet.yml/badge.svg?branch=main) |

# SharpPersistence

SharpPersistence is a robust, extensible, and thoroughly tested .NET library for working with relational database
systems.

### Installation

To install, run one of the following commands:

```bash
dotnet add package SharpPersistence
```

and

```bash
dotnet add package SharpPersistence.Abstractions
```

Or visit [SharpPersistence on NuGet](https://www.nuget.org/packages/SharpPersistence/)
and [SharpPersistence.Abstractions on NuGet](https://www.nuget.org/packages/SharpPersistence.Abstractions/)

## Features

- **`RepositoryBase` with `IRepositoryBase`**

    - All CRUD operations with async support
    - Multiple overloads for flexible querying
    - Proper EF Core tracking management
    - Dependency injection ready
    - Readable, maintainable, and well-formatted code

- **`UnitOfWork` with `IUnitOfWork`**

    - Transactional management of multiple repository operations
    - Ensures atomicity and consistency across changes
    - Integrates seamlessly with the repository pattern
    - Simplifies commit/rollback logic in service layers

- **SQL Check Constraint Generator (`SqlCheckConstrainGenerator`)**
    - Type-safe, raw SQL check constraint generation
    - Supports multiple RDBMS (SQL Server, PostgreSQL, MySQL)
    - Naming convention support (PascalCase, lower_snake_case, UPPER_SNAKE_CASE)
    - Handles all SQL comparison, logical, and set operators
    - Proper string escaping and identifier delimiting
    - Fully tested for edge cases and all method combinations

## `SqlParser` Usage:

- `sql` statements must be enclosed by `-- #start#` and `-- #end#`

```sql
-- #start# GetAllUsers
SELECT *
FROM users
-- #end# GetAllUsers

-- #start# GetActiveUsers
SELECT *
FROM users
WHERE active = 1
-- #end# GetActiveUsers
```

- Unique tag of a sql statement is case-insensitive.
- Paste the following code snippet inside the `Project` node of your `csproj` file to enable scanning of sql files:
- The default directory in the output directory is `sharp_persistence_sql_files`

```xml

<PropertyGroup>
    <DefaultItemExcludes>
        $(DefaultItemExcludes);out/**;publish/**;bin/**;obj/**
    </DefaultItemExcludes>
</PropertyGroup>

<ItemGroup>
<Content
        Include="**\*.sql"
        Exclude="$(DefaultItemExcludes);$(DefaultExcludesInProjectFolder)"
        CopyToOutputDirectory="PreserveNewest"
        TargetPath="sharp_persistence_sql_files\%(RecursiveDir)\%(Filename)%(Extension)"
/>
</ItemGroup>
```

- For ASP.NET Core Dependency Injection:

```csharp
IParsedSqlStorage parsedSqlStatements = new SqlParser().ParseFromStorage();
builder.Services.AddSingleton<IParsedSqlStorage>(parsedSqlStatements);
```

## Testing

- All features are covered by xUnit v3 and Shouldly assertions.
- Tests cover all overloads, edge cases, and RDBMS/naming convention combinations.
- To run tests:
  ```bash
  dotnet test
  ```

## Contributing

Contributions are welcome! Please open issues or submit pull requests for bug fixes, improvements, or new features.
