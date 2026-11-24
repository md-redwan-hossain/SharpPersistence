---
layout: home

hero:
  name: SharpPersistence
  text: Practical .NET persistence, SQL tools & C#-first migrations
  tagline: Four focused building blocks for .NET apps—composable repositories & Unit of Work, type-safe SQL expression builders, a tagged .sql file parser, and a C#-first database migrator for PostgreSQL & Oracle.
  actions:
    - theme: brand
      text: Installation & Packages
      link: /getting-started/installation
    - theme: alt
      text: CLI Reference
      link: /cli/commands

features:
  - title: Repository & Unit of Work
    details: Clean, composable queries via IRepositoryCore<T> and IEntityQuery<T>—non-tracking by default, bulk deletes, named query filters, and transaction-aware IUnitOfWork.
    link: /getting-started/quick-start-repositories
  - title: SQL Expression & Check Builders
    details: Write type-safe, dialect-aware SQL check constraints, index filters, and computed columns in C# for PostgreSQL, Oracle, SQL Server, MySQL, and SQLite.
    link: /getting-started/quick-start-expressions
  - title: Tagged SQL File Parser
    details: Keep raw SQL queries in .sql files with --@tag markers, parse and validate them once via SqlParser, and look them up by tag through IParsedSqlStorage.
    link: /core/sql-parser
  - title: C#-First Database Migrator
    details: Define tables in plain C# with compile-time entity discovery, generate safe-to-re-run SQL scripts for PostgreSQL & Oracle, validate with db-migrator doctor, and bridge to EF Core.
    link: /getting-started/quick-start-migrator
---
