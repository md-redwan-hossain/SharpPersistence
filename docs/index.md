---
layout: default
---

| Branch | Status                                                                                                                |
| ------ | --------------------------------------------------------------------------------------------------------------------- |
| main   | ![Dotnet 9](https://github.com/md-redwan-hossain/SharpPersistence/actions/workflows/dotnet.yml/badge.svg?branch=main) |

# SharpPersistence

SharpPersistence is a collection of robust, extensible, and thoroughly tested .NET libraries for working with database
systems.

### Installation

To install the packages, run the following commands:

```bash
dotnet add package SharpPersistence.Abstractions
dotnet add package SharpPersistence
dotnet add package SharpPersistence.EfCore
```

Or visit:

- [SharpPersistence.Abstractions on NuGet](https://www.nuget.org/packages/SharpPersistence.Abstractions/)

- [SharpPersistence on NuGet](https://www.nuget.org/packages/SharpPersistence/)

- [SharpPersistence.EfCore on NuGet](https://www.nuget.org/packages/SharpPersistence.EfCore/)

## Features

### **`SqlParser`**

- `SqlParser` is available in `SharpPersistence`
- Separation of Concerns: Keep complex SQL queries in `.sql` files instead of cluttering your C# code
- Better SQL Editing: Full syntax highlighting, IntelliSense, and formatting in SQL files
- Version Control Friendly: Track SQL changes separately from business logic
- Team Collaboration: Database developers can work on SQL files while C# developers focus on application logic
- Compile-Time Safety: Parse and validate SQL files at application startup, catching errors early

#### Basic Usage

**1. Create SQL files with tagged blocks:**

- SQL statements must be enclosed by `-- #start# TagName` and `-- #end# TagName` comments.
- Unique tag of a sql statement is case-insensitive.

```sql
-- #start# GetAllUsers
SELECT u.Id, u.Name, u.Email, u.CreatedAt
FROM Users u
WHERE u.Id = :userId
ORDER BY u.Name
-- #end# GetAllUsers

-- #start# GetActiveUsers
SELECT u.Id, u.Name, u.Email
FROM Users u
WHERE u.IsActive = 1 AND u.IsDeleted = 0
ORDER BY u.LastLoginDate DESC
-- #end# GetActiveUsers
```

**2. Configure your project to copy SQL files:**

- Create a file named `Directory.Build.props` at the same level of your `sln` or `slnx` file.
- Copy and paste the following code in the `Directory.Build.props` file.
- If you already have `Directory.Build.props`, then copy the code inside `<Project> </Project>`

```xml

<Project>
    <PropertyGroup>
        <DefaultItemExcludes>
            $(DefaultItemExcludes);out/**;publish/**;bin/**;obj/**
        </DefaultItemExcludes>
    </PropertyGroup>

    <ItemGroup>
        <Content Include="**\*.sql"
                 Exclude="$(DefaultItemExcludes);$(DefaultExcludesInProjectFolder)"
                 CopyToOutputDirectory="PreserveNewest"
                 TargetPath="sharp_persistence_sql_files\$(AssemblyName)\%(RecursiveDir)\%(Filename)%(Extension)"/>
    </ItemGroup>
</Project>

```

- Alternatively, if you want to handle it from `csproj` file, Add the following code to your `csproj` file's `<Project> </Project>` node.

```xml

<PropertyGroup>
    <DefaultItemExcludes>
        $(DefaultItemExcludes);out/**;publish/**;bin/**;obj/**
    </DefaultItemExcludes>
</PropertyGroup>

<ItemGroup>
<Content Include="**\*.sql"
         Exclude="$(DefaultItemExcludes);$(DefaultExcludesInProjectFolder)"
         CopyToOutputDirectory="PreserveNewest"
         TargetPath="sharp_persistence_sql_files\$(AssemblyName)\%(RecursiveDir)\%(Filename)%(Extension)"/>
</ItemGroup>
```

- `sharp_persistence_sql_files` is the default directory. If you want to change it, make sure to pass the new value in
  the `ParseFromStorage()` method's parameter. The same value must be used in the `TargetPath` of the above code.

**3. Parse and use in your application:**

```csharp
// Parse SQL files at startup
IParsedSqlStorage parsedSqlStorage = new SqlParser().ParseFromStorage();

// Access parsed SQL by tag name (case-insensitive) through C# indexer
string getAllUsersQuery = parsedSqlStorage["GetAllUsers"];
// Use the query...

// Safe access with TryGet
if (parsedSqlStorage.TryGetParsedSql("GetAllUsers", out string? query))
{
    // Use the query...
}
```

**4. Dependency Injection (ASP.NET Core):**

```csharp
// In Program.cs
IParsedSqlStorage parsedSqlStatements = new SqlParser().ParseFromStorage();
builder.Services.AddSingleton<IParsedSqlStorage>(parsedSqlStatements);

// In your service classes
public class UserService
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly IParsedSqlStorage _parsedSqlStorage;

    public UserService(IDbConnectionFactory dbConnectionFactory, IParsedSqlStorage sqlStorage)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _parsedSqlStorage = parsedSqlStorage;
    }

    public async Task<IEnumerable<User>> GetAllUsersAsync(int userId, CancellationToken ct)
    {
        var sql = _parsedSqlStorage["GetAllUsers"];

        await using var connection = await _dbConnectionFactory.CreateConnectionAsync(ct);

        var parameters = new { userId };

        return await connection.QueryAsync<User>(new CommandDefinition(sql, parameters,
            cancellationToken: ct));
    }
}
```

**Notes:**
SqlParser validates your SQL files at startup and throws detailed error messages for:

- Missing start/end tags
- Duplicate tag names
- Invalid tag formats

### **`SqlCheckConstrainGenerator`**

- `SqlCheckConstrainGenerator` is available in `SharpPersistence`
- Type-safe, raw SQL check constraint generation
- Supports multiple RDBMS (SQL Server, PostgreSQL, MySQL)
- Naming convention support (PascalCase, lower_snake_case, UPPER_SNAKE_CASE)
- Handles all SQL comparison, logical, and set operators
- Proper string escaping and identifier delimiting
- Fully tested for edge cases and all method combinations

Some examples with `IEntityTypeConfiguration` of Ef Core are given below:

```csharp
  var cc = new SqlCheckConstrainGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
      delimitStringGlobalLevel: false);

  builder.ToTable(x => x.HasCheckConstraint(
      "valid_product_sell_price_in_sales_invoice",
      cc.GreaterThanOrEqual(nameof(SalesInvoiceItem.SingleUnitSellPrice),
          0, SqlDataType.Decimal)
  ));

  builder.ToTable(x => x.HasCheckConstraint(
    "valid_employee_release_data",
    cc.Or(
        cc.And(
            cc.EqualTo(nameof(EmploymentRecord.IsReleased), true),
            cc.IsNotNull(nameof(EmploymentRecord.ReleaseDate)),
            cc.IsNotNull(nameof(EmploymentRecord.ReleaseReasonId))
        ),
        cc.And(
            cc.EqualTo(nameof(EmploymentRecord.IsReleased), false),
            cc.IsNull(nameof(EmploymentRecord.ReleaseDate)),
            cc.IsNull(nameof(EmploymentRecord.ReleaseReasonId))
        )
    )
));

  builder.ToTable(x => x.HasCheckConstraint(
    "valid_debit_credit_entry",
    cc.Or(
        cc.And(
            cc.GreaterThanOrEqual(nameof(JournalVoucherItem.CreditAmount),
                0, SqlDataType.Decimal),
            cc.EqualTo(nameof(JournalVoucherItem.DebitAmount),
                0, SqlDataType.Decimal)
        ),
        cc.And(
            cc.GreaterThanOrEqual(nameof(JournalVoucherItem.DebitAmount),
                0, SqlDataType.Decimal),
            cc.EqualTo(nameof(JournalVoucherItem.CreditAmount),
                0, SqlDataType.Decimal)
        )
    )
));
```

### **`RepositoryBase` with `IRepositoryBase`**

- `IRepositoryBase` is available in `SharpPersistence.Abstractions`
- `RepositoryBase` is available in `SharpPersistence.EfCore`
- All CRUD operations with async support
- Multiple overloads for flexible querying
- Proper EF Core tracking management
- Dependency injection ready
- Readable, maintainable, and well-formatted code

### **`UnitOfWork` with `IUnitOfWork`**

- `IUnitOfWork` is available in `SharpPersistence.Abstractions`
- `UnitOfWork` is available in `SharpPersistence.EfCore`
- Transactional management of multiple repository operations
- Ensures atomicity and consistency across changes
- Integrates seamlessly with the repository pattern
- Simplifies commit/rollback logic in service layers

## Testing

- All features are covered by xUnit v3 and Shouldly assertions.
- Tests cover all overloads, edge cases, and RDBMS/naming convention combinations.
- To run tests:
  ```bash
  dotnet test
  ```

## Contributing

Contributions are welcome! Please open issues or submit pull requests for bug fixes, improvements, or new features.
