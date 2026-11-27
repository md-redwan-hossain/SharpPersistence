import { defineConfig } from 'vitepress';

export default defineConfig({
  title: 'SharpPersistence',
  description:
    'Composable .NET persistence abstractions, EF Core repositories, SQL expression builders, SQL file parsing, and C#-first database migrations for PostgreSQL and Oracle.',
  base: '/SharpPersistence/',
  cleanUrls: true,
  lastUpdated: true,

  themeConfig: {
    nav: [
      { text: 'Getting Started', link: '/getting-started/installation' },
      { text: 'Core & EF Core', link: '/core/abstractions' },
      { text: 'Migrator', link: '/migrator/setup-and-di' },
      { text: 'EF Bridge', link: '/migrator-efcore/model-bridge' },
      { text: 'CLI', link: '/cli/commands' }
    ],

    sidebar: [
      {
        text: 'Getting Started',
        items: [
          { text: 'Installation & Packages', link: '/getting-started/installation' },
          { text: 'Quick Start: Repositories & Unit of Work', link: '/getting-started/quick-start-repositories' },
          { text: 'Quick Start: SQL Expressions', link: '/getting-started/quick-start-expressions' },
          { text: 'Quick Start: Database Migrator', link: '/getting-started/quick-start-migrator' }
        ]
      },
      {
        text: 'Core & Persistence',
        items: [
          { text: 'Abstractions & Domain Markers', link: '/core/abstractions' },
          { text: 'EF Core Repositories & Unit of Work', link: '/core/efcore-repositories' },
          { text: 'SQL Expression & Check Builders', link: '/core/check-constraint-builder' },
          { text: 'SQL Parser & ParsedSqlStorage', link: '/core/sql-parser' }
        ]
      },
      {
        text: 'Database Migrator',
        items: [
          { text: 'Setup, DI & Runtime', link: '/migrator/setup-and-di' },
          { text: 'Global Conventions & Entity Replacements', link: '/migrator/conventions-and-stubs' },
          { text: 'Entities and Configurations', link: '/migrator/entities-and-configurations' },
          { text: 'Relationships & Indexes', link: '/migrator/relationships-and-indexes' },
          { text: 'Constraints & Sequences', link: '/migrator/constraints-and-sequences' },
          { text: 'PostgreSQL Features', link: '/migrator/postgresql' },
          { text: 'Oracle Features', link: '/migrator/oracle' }
        ]
      },
      {
        text: 'EF Core Bridge',
        items: [
          { text: 'Runtime Model Bridge', link: '/migrator-efcore/model-bridge' },
          { text: 'Migrating from EF Core', link: '/migrator-efcore/migrating-from-efcore' }
        ]
      },
      {
        text: 'CLI Reference',
        items: [
          { text: 'db-migrator Commands', link: '/cli/commands' }
        ]
      }
    ],

    search: {
      provider: 'local'
    },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/md-redwan-hossain/SharpPersistence' }
    ],

    footer: {
      message: 'Released under the Apache-2.0 License.',
      copyright: 'Copyright © Md. Redwan Hossain'
    }
  }
});
