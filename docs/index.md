---
layout: home

hero:
  name: SharpPersistence
  text: Type-safe .NET persistence & C#-first SQL migrations
  tagline: Composable EF Core repositories, dialect-aware SQL check constraints, compile-time entity discovery, and idempotent PostgreSQL & Oracle migrations without runtime EF Core model-building overhead.
  actions:
    - theme: brand
      text: Get Started
      link: /getting-started/installation
    - theme: alt
      text: 5-Minute Quick Start
      link: /getting-started/quick-start
    - theme: alt
      text: CLI Reference
      link: /cli/commands

features:
  - title: Composable Repositories & UoW
    details: Query cleanly with IRepositoryCore<TEntity> and IEntityQuery<T>—non-tracking by default, bulk deletes, named query filters, and transaction-aware IUnitOfWork.
    link: /core/efcore-repositories
  - title: Type-Safe SQL Check Constraints
    details: Build dialect-aware CHECK expressions in C# for PostgreSQL, Oracle, SQL Server, MySQL, and SQLite with arithmetic, CASE/IF, string helpers, and raw predicates.
    link: /core/check-constraint-builder
  - title: C#-First Database Migrator
    details: Define schemas with IEntityRegistry and IEntityConfiguration<T>, generate idempotent SQL scripts, detect drift, and serialize runs with advisory/DBMS locks.
    link: /migrator/setup-and-di
  - title: Compile-Time Entity Discovery
    details: Zero reflection at startup. Roslyn source generators pair [EntityConfiguration<T>] classes, wire [EntityRegistry]s across projects, and catch mistakes at build time.
    link: /migrator/source-generator
  - title: First-Class PostgreSQL & Oracle
    details: Native support for PostgreSQL extensions, enums, GiST exclusion constraints, unlogged tables, and tablespaces alongside Oracle 19c/23ai PL/SQL idempotency.
    link: /migrator/postgresql
  - title: Drop-In EF Core Bridge & Doctor CLI
    details: Reuse your C# schema inside DbContext via ApplySchemaToEfCore, translate existing EF Core configs/migrations, and validate DI & live DBs with db-migrator doctor.
    link: /migrator-efcore/migrating-from-efcore
---
