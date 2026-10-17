/**
 * One link in the curated documentation navigation: the label shown and the page slug under the docs root.
 */
export type NavLink = Readonly<{ label: string; slug: string }>;

/**
 * One labelled group of the curated documentation navigation.
 */
export type NavGroup = Readonly<{ heading: string; links: readonly NavLink[] }>;

/**
 * The short, Fuse-style navigation shown above the full page tree.
 *
 * Only the core pages a reader needs first are listed here. Every other page stays published, searchable and in the
 * plain-text indexes, and the sidebar lists it under a collapsed "More" group.
 */
export const curatedNavigation: readonly NavGroup[] = [
  {
    heading: 'Start Here',
    links: [
      { label: 'Overview', slug: '' },
      { label: 'Getting Started', slug: 'getting-started' },
      { label: 'Cheat Sheet', slug: 'getting-started/cheat-sheet' },
      { label: 'Coming From MediatR', slug: 'migration/mediatr-differences' },
    ],
  },
  {
    heading: 'Understand',
    links: [
      { label: 'Commands', slug: 'concepts/commands' },
      { label: 'Queries', slug: 'concepts/queries' },
      { label: 'Events', slug: 'concepts/events' },
      { label: 'Handler Pipeline', slug: 'concepts/handler-pipeline' },
      { label: 'Durable Messaging', slug: 'reliable-messaging' },
      { label: 'Inbox', slug: 'reliable-messaging/inbox' },
      { label: 'Outbox', slug: 'reliable-messaging/outbox' },
      { label: 'Sagas', slug: 'reliable-messaging/saga' },
      { label: 'Limits', slug: 'reference/limits' },
    ],
  },
  {
    heading: 'Operate',
    links: [
      { label: 'Hosting', slug: 'architecture/hosted-services' },
      { label: 'Diagnostics and Health', slug: 'operations/diagnostics-and-health' },
      { label: 'Troubleshooting', slug: 'operations/troubleshooting' },
      { label: 'Production Runbook', slug: 'operations/runbook' },
    ],
  },
  {
    heading: 'Reference',
    links: [
      { label: 'Packages', slug: 'architecture/dependency-graph' },
      { label: 'Analyzers', slug: 'reference/analyzers' },
      { label: 'Glossary', slug: 'reference/glossary' },
    ],
  },
  {
    heading: 'Contribute',
    links: [
      { label: 'Architecture', slug: 'architecture' },
      { label: 'Contributing', slug: 'contributing' },
      { label: 'Roadmap', slug: 'roadmap' },
    ],
  },
];
