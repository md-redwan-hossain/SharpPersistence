| Branch | Status                                                                                                                |
|--------|-----------------------------------------------------------------------------------------------------------------------|
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

### **`RepositoryBase` with `IRepositoryBase`**

- All CRUD operations with async support
- Multiple overloads for flexible querying
- Proper EF Core tracking management
- Dependency injection ready
- Readable, maintainable, and well-formatted code

### **`UnitOfWork` with `IUnitOfWork`**

- Transactional management of multiple repository operations
- Ensures atomicity and consistency across changes
- Integrates seamlessly with the repository pattern
- Simplifies commit/rollback logic in service layers

### **`SqlCheckConstrainGenerator`**

- Type-safe, raw SQL check constraint generation
- Supports multiple RDBMS (SQL Server, PostgreSQL, MySQL)
- Naming convention support (PascalCase, lower_snake_case, UPPER_SNAKE_CASE)
- Handles all SQL comparison, logical, and set operators
- Proper string escaping and identifier delimiting
- Fully tested for edge cases and all method combinations

### **`SqlParser`**

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
WHERE u.IsDeleted = 0
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

- Add this to your `.csproj` file's `Project` node to automatically copy SQL files to the output directory.
- `sharp_persistence_sql_files` is the default directory. If you want to change it, make sure to pass the new value in
  the `ParseFromStorage()` method's parameter. The same value must be used in the `TargetPath` below.

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
         TargetPath="sharp_persistence_sql_files\%(RecursiveDir)\%(Filename)%(Extension)"/>
</ItemGroup>
```

- Don't miss out the `Project` node during pasting the code. An example is given below:

```xml

<Project Sdk="Microsoft.NET.Sdk">
    <!-- paste it here -->
</Project>
```

**3. Parse and use in your application:**

```csharp
// Parse SQL files at startup
IParsedSqlStorage sqlStorage = new SqlParser().ParseFromStorage();

// Access parsed SQL by tag name (case-insensitive)
string getAllUsersQuery = sqlStorage["GetAllUsers"];
// Use the query...

// Safe access with TryGet
if (sqlStorage.TryGetParsedSql("GetAllUsers", out string? query))
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
    private readonly IParsedSqlStorage _sqlStorage;

    public UserService(IParsedSqlStorage sqlStorage)
    {
        _sqlStorage = sqlStorage;
    }

    public async Task<List<User>> GetAllUsersAsync()
    {
        var sql = _sqlStorage["GetAllUsers"];
        return await _dbContext.Users.FromSqlRaw(sql).ToListAsync();
    }
}
```

**Error Handling:**
SqlParser validates your SQL files at startup and throws detailed error messages for:

- Missing start/end tags
- Duplicate tag names
- Invalid tag formats

## Testing

- All features are covered by xUnit v3 and Shouldly assertions.
- Tests cover all overloads, edge cases, and RDBMS/naming convention combinations.
- To run tests:
  ```bash
  dotnet test
  ```

## Contributing

Contributions are welcome! Please open issues or submit pull requests for bug fixes, improvements, or new features.
