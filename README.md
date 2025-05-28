# SharpPersistence

A robust, extensible, and thoroughly tested .NET library for working with relational database systems.

## Features

- **`RepositoryBase` with `IRepositoryBase`**

  - All CRUD operations with async support
  - Multiple overloads for flexible querying
  - Proper EF Core tracking management
  - Dependency injection ready
  - Readable, maintainable, and well-formatted code

- **SQL Check Constraint Generator (`SqlCheckConstrainGenerator`)**
  - Type-safe, raw SQL check constraint generation
  - Supports multiple RDBMS (SQL Server, PostgreSQL, MySQL)
  - Naming convention support (PascalCase, lower_snake_case, UPPER_SNAKE_CASE)
  - Handles all SQL comparison, logical, and set operators
  - Proper string escaping and identifier delimiting
  - Fully tested for edge cases and all method combinations

## Testing

- All features are covered by xUnit v3 and Shouldly assertions.
- Tests cover all overloads, edge cases, and RDBMS/naming convention combinations.
- To run tests:
  ```bash
  dotnet test
  ```

## Contributing

Contributions are welcome! Please open issues or submit pull requests for bug fixes, improvements, or new features.
