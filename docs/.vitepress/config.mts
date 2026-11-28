import { defineConfig } from 'vitepress';

export default defineConfig({
  title: 'SharpPersistence',
  description:
    'Composable .NET persistence abstractions, EF Core repositories, SQL expression builders, SQL file parsing, and C#-first database migrations for PostgreSQL and Oracle.',
  base: '/SharpPersistence/',
  cleanUrls: true,
  lastUpdated: true,
  head: [
    [
      'link',
      {
        rel: 'icon',
        type: 'image/png',
        href: 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAEdklEQVR4AexWe0ybVRT/9clzLQNGHQzGeIg43Cqrc4IDKzIRM03MMIiPZEwXM00kxhhjjPqXJBpj/MNEjXGbWTY1KmOZOpQNs9I5NsdwgzEfkxFGCmW0sPEubT3n0q/pgxZYWJYl+/L9zr333NfvnnvO+T65+wY/ctzg5xaBm9MCh3qsSNrdgKRdM6jrsghPGpp0oGdkXIDrQjmHuCYLWMcmMTA+hYGJGfRRm/dZ991RpO1pFFj/g4lVcyIsgarGU9hACzG2/dY252ITTqd3jG/dq5ylEpbA4d7LaLEOCTT1Dnqnp8ZGIUcbg5SYSOiiIrBySZS3b6GVsARCLWZMScT5px7E6S3F2JyuQ3maDmPTTsSolKjIWC7wdPYKON1uJH/1CxJ2HkLlr6dmXS4sAa1aBblMBgUhPkKFwCc+UoUfu60o2m+G8cAx7C3Jx7ebDAK19+bCRQQs5B82cs7+8cnA6aIdkoCdJt0RFwv71jJMbX8UXxr1+PjsfwKftHdh1DGNaZcLlrEJmPpsWEKnNyTFiUUlweTXJmiwkq4sMVItqf3KIAJvtnSiuqkNTzScxAf33QmNWimssOuvHtSYOwRebm7Hn4NXhD5CIYeMloxSKkj6v2y5topidFQaQcaA279btORC+ojdtNFOwvCUAzlkAZ+uoKpKLsfo8+XCQvVl9wT1S4oYIseO+sW5bly4MiqpRRlEQCHn80CcTozwCJmnlIoDF/txla6BE47D5RbjXzx6BrlfNwlsJL+gD60Yzif/vLMb26k/0BmDCJypeAB9z22CPkGL4/12sQCLHXnp2PdQvsA3petgSNIie+8RJFI2rPbkiLbLwzg/NCLAc528M08m+9PLNXEVouIRQQTiyNt10RHQJ2pQ/lMLCuqaUVRvRjSZsTIrBYwnM5OxJSMZhmVa8OMkC3AZCjKZDK/pM1Fz1ypU56b5DQsiIPWes10FR8LvZAWTxQYp3Ur9XBYnJ+CNu7PwtuF2blJiikIchS6DnfPS6LjQs3jXkIOPCvOwY3U6N70IScA7YpZKp30EB7v7UdfVh5o1GciL14hR3z9sgL26TOBCVQleMbfDZBlEMx3ATKFqo2+HGOgjQhLQ0lUoyXQMNXl7LMW5NO+tE53Y/PMJsHUaegaEesLpwmnyAclSfI2cHYvqj2EjXeH9+81454+/xVhfEZLAe+tzMfZCuQCHWjblfmmi5Fvc/qzjIp453IrHiVA+fQ1rW/9htcB0gG84KHGJDh8RkgAdHhznDKUnNKV5G3RLYaT7Z7xPyWoPpeBPi9Zga04qCpfHYyFPSALhFnldn4UjjxUIFN42s+EqTbRI1xwhPJdPaw3I/zEqBXf54ZoI+K0wS4MT0Eums/iwYDUuPVvqRS1da+DwRSXAkVHV2IrSg8dRsmIZylKTKDQjvVArgrcL1gRSXED7JP287Pu3F/wjUzxPX1hUAlq1EhoK16UUwhy68+G+qAReXZuJ4W2PwEb/EPEhvv+BpBaVQODi82nfInDdLTDXNfwPAAD//zrjD3AAAAAGSURBVAMA6P0M36JsYnMAAAAASUVORK5CYII='
      }
    ]
  ],

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
          { text: 'Global Conventions & Entity Replacements', link: '/migrator/conventions-and-entity-replacements' },
          { text: 'Entities and Configurations', link: '/migrator/entities-and-configurations' },
          { text: 'Relationships & Indexes', link: '/migrator/relationships-and-indexes' },
          { text: 'Constraints & Sequences', link: '/migrator/constraints-and-sequences' },
          { text: 'PostgreSQL Features', link: '/migrator/postgresql' },
          { text: 'Oracle Features', link: '/migrator/oracle' },
          { text: 'Implementation Details', link: '/migrator/implementation-details' }
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
