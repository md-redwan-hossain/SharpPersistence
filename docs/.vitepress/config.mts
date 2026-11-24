import { defineConfig } from 'vitepress';

export default defineConfig({
  title: 'SharpPersistence',
  description:
    'Composable .NET persistence abstractions, EF Core repositories, dialect-aware SQL check constraints, and C#-first database migrations for PostgreSQL and Oracle.',
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
          { text: '5-Minute Quick Start', link: '/getting-started/quick-start' }
        ]
      },
      {
        text: 'Core & Persistence',
        items: [
          { text: 'Abstractions & Discovery', link: '/core/abstractions' },
          { text: 'EF Core Repositories & UoW', link: '/core/efcore-repositories' },
          { text: 'SQL Check Constraint Builder', link: '/core/check-constraint-builder' }
        ]
      },
      {
        text: 'Database Migrator',
        items: [
          { text: 'Setup, DI & Runtime', link: '/migrator/setup-and-di' },
          { text: 'Entities, Keys & Columns', link: '/migrator/entity-configuration' },
          { text: 'Relationships & Indexes', link: '/migrator/relationships-and-indexes' },
          { text: 'Constraints & Sequences', link: '/migrator/constraints-and-sequences' },
          { text: 'PostgreSQL Features', link: '/migrator/postgresql' },
          { text: 'Oracle Features', link: '/migrator/oracle' },
          { text: 'Compile-Time Discovery', link: '/migrator/source-generator' }
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
