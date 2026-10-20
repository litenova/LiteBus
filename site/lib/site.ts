export const siteUrl = 'https://litebus.io';

export const siteName = 'LiteBus';

export const siteTagline = 'Mediator and durable messaging for .NET';

export const siteTitle = `${siteName} - ${siteTagline}`;

export const siteDescription =
  'LiteBus is a mediator and durable messaging library for .NET. Compose commands, queries, events, inbox, and outbox processing from explicit contracts and opt-in adapters.';

export const siteOgImage = '/og.png';

/** The LiteBus release the documentation describes. Updated in the commit that cuts a release. */
export const latestRelease = '7.0.0-preview.7';

/** Where the documentation is served. */
export const docsBasePath = '/docs';

/** The Markdown content root, relative to the site directory. */
export const docsContentDir = 'content/docs';

/** Git ref used for fallback links into the repository from documentation pages. */
export const sourceRef = 'v7';

export const repositoryUrl = 'https://github.com/litenova/LiteBus';

/** The changelog page, published from the repository Changelog.md by scripts/sync-changelog.mjs. */
export const changelogUrl = `${docsBasePath}/changelog`;

export const nugetUrl = 'https://www.nuget.org/packages?q=LiteBus';

export const companyName = 'LiteNova Solutions';

export const companyUrl = 'https://www.litenova.solutions';

export const authorName = companyName;

export const authorUrl = companyUrl;

export const siteKeywords = [
  'LiteBus',
  '.NET',
  'dotnet',
  'mediator',
  'CQRS',
  'commands',
  'queries',
  'events',
  'inbox',
  'outbox',
  'saga',
  'durable messaging',
  'message pipeline',
  'RabbitMQ',
  'Kafka',
  'Azure Service Bus',
  'AWS SQS',
];
