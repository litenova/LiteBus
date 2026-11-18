/** The public origin of the site. */
export const siteUrl = 'https://litebus.io';

/** The product name shown in the header and titles. */
export const siteName = 'LiteBus';

/** The one-line statement of what the library does. */
export const siteTagline = 'Mediator and durable messaging for .NET';

/** The home page title. */
export const siteTitle = `${siteName} - ${siteTagline}`;

/** The fallback description for pages and meta tags. */
export const siteDescription =
  'LiteBus is a mediator and durable messaging library for .NET. Compose commands, queries, events, inbox, and outbox processing from explicit contracts and opt-in adapters.';

/** The social preview image path. */
export const siteOgImage = '/og.png';

/** The LiteBus release the documentation describes. Updated in the commit that cuts a release. */
export const latestRelease = '8.0.0';

/** Where the documentation is served. */
export const docsBasePath = '/docs';

/** The Markdown content root, relative to the site directory. */
export const docsContentDir = 'content/docs';

/** The target framework fact the home page shows. */
export const targetFramework = '.NET 10';

/** Git ref used for fallback links into the repository from documentation pages. */
export const sourceRef = 'v7';

/** The repository that holds the library and the documentation sources. */
export const repositoryUrl = 'https://github.com/litenova/LiteBus';

/** The changelog page, published from the repository Changelog.md by scripts/sync-changelog.mjs. */
export const changelogUrl = `${docsBasePath}/changelog`;

/** The package search for the published LiteBus packages. */
export const nugetUrl = 'https://www.nuget.org/packages?q=LiteBus';

/** The company behind the library. */
export const companyName = 'LiteNova Solutions';

/** The company site. */
export const companyUrl = 'https://www.litenova.solutions';

/** The site author. */
export const authorName = companyName;

/** The author link. */
export const authorUrl = companyUrl;
