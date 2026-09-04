import type { Item, Node, Root } from 'fumadocs-core/page-tree';
import { llms } from 'fumadocs-core/source/llms';
import { type DocsPage, markdownHref, repositorySourcesUrl, resolveDocHref } from '@/lib/doc-links';
import { repositoryUrl, siteUrl } from '@/lib/site';
import { type DocsSource, getSource } from '@/lib/source';
import { type DocsVersion, defaultVersion, docsVersions, versionBasePath } from '@/lib/versions';

/**
 * The plain-text views of the documentation that agents read.
 *
 * Three endpoints per version, following the llms.txt convention (https://llmstxt.org):
 *
 * - `llms.txt` is an index: a short summary of the library, then one link per page with a note.
 * - `llms-full.txt` is every page's complete Markdown in one document.
 * - `<page>.md` is one page's Markdown, which is what the links in the two files above point at.
 *
 * The index is part hand-written and part generated. The summary, the orientation prose, and the `Start Here` list
 * are written here because a client arriving with no context needs the shape of the library before it needs a file
 * listing. Everything below `Start Here` comes from the same page tree the navigation uses, so a page added to
 * `meta.json` appears here without anyone remembering to add it.
 */

/**
 * Sections placed under the `Optional` heading.
 *
 * The convention reserves that heading for pages a client can skip when its context is tight. The roadmap describes
 * work that has not shipped and the contributing guide describes this repository's process, so neither answers a
 * question about how to use the library.
 */
const optionalSections = ['roadmap', 'contributing'] as const;

/** A section of the index: an H2 heading and the page tree nodes listed under it. */
type IndexSection = {
  heading: string;
  nodes: Node[];
};

/** A hand-written entry in `Start Here`. */
type CuratedEntry = {
  slug: string[];
  note: string;
};

/**
 * The pages to read first, in order, with the reason to read each.
 *
 * These notes describe what a page is for rather than what it says, so they stay true as the pages are edited. Every
 * slug is resolved against the version being rendered and a missing one fails the build, which is what keeps the list
 * from quietly rotting as pages move.
 */
function curatedEntries(version: DocsVersion): CuratedEntry[] {
  return [
    {
      slug: [],
      note: 'Documentation index. The map of every area, and the table of which page answers which goal.',
    },
    {
      slug: ['getting-started'],
      note: 'Install the packages, register the modules, write a first handler, and send a first message.',
    },
    {
      slug: ['reference', `feature-index-${version.id}`],
      note: 'Capability to documentation to package, one table per axis. The fastest way to find the package a feature needs.',
    },
    {
      slug: ['reference', 'capability-catalog'],
      note: 'Every shipped and planned capability with a stable identifier, maturity tier, package mapping, and deep link.',
    },
    {
      slug: ['architecture', 'dependency-graph'],
      note: 'Which packages to install for a given combination of modules, container, storage, and broker.',
    },
    {
      slug: ['getting-started', 'cheat-sheet'],
      note: 'Contract interfaces, handler interfaces, builder calls, and mediator signatures in one place.',
    },
    {
      slug: ['reference', 'glossary'],
      note: 'The exact meaning of the terms these pages use, including the ones that differ from other messaging libraries.',
    },
    {
      slug: ['migration', 'from-mediatr'],
      note: 'The MediatR equivalences, for translating an existing application rather than starting a new one.',
    },
  ];
}

/** The name of a page tree node, which these trees always give as a plain string. */
function nodeName(node: { name?: unknown }): string | undefined {
  return typeof node.name === 'string' && node.name.length > 0 ? node.name : undefined;
}

/** The documentation path a node leads to, used to decide which section it belongs in. */
function nodeUrl(node: Node): string | undefined {
  if (node.type === 'page') {
    return node.url;
  }

  if (node.type === 'folder') {
    return node.index?.url ?? firstPage(node.children)?.url;
  }

  return undefined;
}

/** The first page reachable under a list of nodes, in tree order. */
function firstPage(nodes: readonly Node[]): Item | undefined {
  for (const page of walkPages(nodes)) {
    return page;
  }

  return undefined;
}

/**
 * Every page under a list of nodes, in the order the navigation shows them.
 *
 * A folder's index page is hoisted onto the folder, so it is emitted ahead of the folder's other children rather than
 * being left out. Callers dedupe on URL: whether an index page also stays in `children` is a page tree detail this
 * does not need to depend on.
 */
function* walkPages(nodes: readonly Node[]): Generator<Item> {
  for (const node of nodes) {
    if (node.type === 'page') {
      yield node;
    } else if (node.type === 'folder') {
      if (node.index !== undefined) {
        yield node.index;
      }

      yield* walkPages(node.children);
    }
  }
}

/**
 * The index's sections, taken from the separators in `meta.json`.
 *
 * The separators already divide the navigation into the groups a reader is offered, and the convention's H2 sections
 * are the same idea, so one drives the other. A node ahead of the first separator is dropped, which means the
 * documentation index page: `Start Here` links it with a better note than its own description makes.
 */
function sectionsFromTree(tree: Root, version: DocsVersion): IndexSection[] {
  const base = versionBasePath(version);
  const optionalPaths = optionalSections.map((section) => `${base}/${section}`);
  const sections: IndexSection[] = [];
  const optional: IndexSection = { heading: 'Optional', nodes: [] };
  let current: IndexSection | undefined;

  for (const child of tree.children) {
    if (child.type === 'separator') {
      current = { heading: nodeName(child) ?? 'Documentation', nodes: [] };
      sections.push(current);
      continue;
    }

    const url = nodeUrl(child);
    const isOptional =
      url !== undefined && optionalPaths.some((path) => url === path || url.startsWith(`${path}/`));

    (isOptional ? optional : current)?.nodes.push(child);
  }

  return [...sections, optional].filter((section) => section.nodes.length > 0);
}

/**
 * Rewrites the site-relative links in generated text to absolute `.md` endpoints.
 *
 * The page tree holds paths, and these files are read away from the site that would resolve them. Pointing at the
 * `.md` endpoint rather than at the page means a client that follows a link gets Markdown instead of an application
 * shell.
 */
function absoluteMarkdownLinks(text: string): string {
  return text.replace(
    /\]\((\/[^)]*)\)/g,
    (_match, href: string) => `](${siteUrl}${markdownHref(href)})`,
  );
}

/** Strips the byte-order mark that at least one source file carries, so output starts at its first heading. */
function withoutByteOrderMark(content: string): string {
  return content.charCodeAt(0) === 0xfeff ? content.slice(1) : content;
}

/**
 * A page's Markdown with every link usable from outside the site.
 *
 * The sources link to each other by relative source path, which resolves to nothing once a page is served on its own
 * or concatenated with every other page. Each one becomes the absolute URL of the target's `.md` endpoint, or the
 * file in the repository when the target is Markdown the site does not serve.
 */
function contentWithAbsoluteLinks(page: DocsPage, source: DocsSource, version: DocsVersion): string {
  return withoutByteOrderMark(page.data.content).replace(
    /\]\(([^)]+)\)/g,
    (match, href: string) => {
      // A link with a title (`](url "title")`) would need splitting before resolution. None of the sources use one,
      // so such a link is left exactly as written rather than guessed at.
      if (/\s/.test(href)) {
        return match;
      }

      const resolved = resolveDocHref(href, page, source, version);

      return resolved.startsWith('/')
        ? `](${siteUrl}${markdownHref(resolved)})`
        : `](${resolved})`;
    },
  );
}

/**
 * Where a version's index is served.
 *
 * The default version answers at the site root, which is the path the convention has clients probe first, so its
 * documentation prefix is not repeated there. Every other line carries its own prefix. `next.config.mjs` redirects
 * `/docs/llms.txt` to the root copy, because the prefixed form is the other reasonable guess.
 */
export function llmsIndexPath(version: DocsVersion): string {
  return version.default ? '/llms.txt' : `${versionBasePath(version)}/llms.txt`;
}

/** Where a version's complete text is served, following `llmsIndexPath`. */
export function llmsFullPath(version: DocsVersion): string {
  return version.default ? '/llms-full.txt' : `${versionBasePath(version)}/llms-full.txt`;
}

/** How a version describes itself, for the lines that have to be honest about which release is documented. */
function releaseLine(version: DocsVersion): string {
  return version.status === 'prerelease'
    ? `LiteBus ${version.release}, a pre-release of the ${version.label} line`
    : `LiteBus ${version.release}, the ${version.label} line`;
}

/** The other documentation lines this site serves, so a client on the wrong one can find the right one. */
function otherVersionLines(version: DocsVersion): string[] {
  return docsVersions
    .filter((candidate) => candidate.id !== version.id)
    .map((candidate) => {
      const suffix = candidate.status === 'prerelease' ? ' (pre-release)' : ' (stable)';
      const index = `${siteUrl}${llmsIndexPath(candidate)}`;

      return `- LiteBus ${candidate.label}${suffix}, release ${candidate.release}: ${index}`;
    });
}

/**
 * The summary and orientation prose above the file lists.
 *
 * The convention allows any Markdown except headings between the summary blockquote and the first H2, and this is what
 * goes there: what the library is, where the durability boundary sits, how the packages are shaped, and what the rest
 * of this file set contains. A client that reads only this far should still be able to choose a package.
 */
function preamble(version: DocsVersion): string[] {
  const lines = [
    '# LiteBus',
    '',
    '> LiteBus is a mediator and durable messaging library for .NET 10, MIT licensed and free for commercial use. It',
    '> provides separate command, query, and event contracts, each with its own mediator, handlers, and pipeline, and',
    '> adds opt-in inbox, outbox, saga, storage, transport, ingress, hosting, and observability modules that persist',
    '> and carry those messages between processes.',
    '',
    `This index describes ${releaseLine(version)}. Every link below points at Markdown rather than at a rendered page:`,
    `appending \`.md\` to any documentation URL returns that page's source. The complete text of every page listed here`,
    `is available as a single document at ${siteUrl}${llmsFullPath(version)}.`,
    '',
    'Mediation and durability are separate decisions. Command, query, and event mediation executes handlers in the',
    'caller\'s process and persists nothing. Durable behavior begins only when an application selects an inbox or',
    'outbox module together with a storage adapter and a processor host; until then no message is written anywhere.',
    'Broker and database integrations ship as separate packages, so an application installs only the SDKs it uses.',
    '',
    'The packages are granular for that reason: one package per module per dependency injection container, and one per',
    'storage or broker adapter. `LiteBus.Commands.Extensions.Microsoft.DependencyInjection` is the command module for',
    'Microsoft dependency injection, and queries, events, and Autofac follow the same shape. The feature index and the',
    'dependency graph linked below map a capability to the packages that provide it.',
    '',
    'Registration composes the modules an application installed:',
    '',
    '```csharp',
    'services.AddLiteBus(liteBus =>',
    '{',
    '    var applicationAssembly = typeof(ProcessPaymentCommand).Assembly;',
    '',
    '    liteBus.AddMessaging(_ =>',
    '    {',
    '    });',
    '',
    '    liteBus.AddCommands(commands => commands.RegisterFromAssembly(applicationAssembly));',
    '    liteBus.AddQueries(queries => queries.RegisterFromAssembly(applicationAssembly));',
    '    liteBus.AddEvents(events => events.RegisterFromAssembly(applicationAssembly));',
    '});',
    '```',
  ];

  if (version.status === 'prerelease') {
    lines.push(
      '',
      `${version.label} is a pre-release and its API can still change. An application that is not specifically`,
      `targeting ${version.release} should read the ${defaultVersion.label} documentation instead, indexed at`,
      `${siteUrl}${llmsIndexPath(defaultVersion)}.`,
    );
  }

  const others = otherVersionLines(version);

  if (others.length > 0) {
    lines.push('', 'Other documentation lines this site serves:', '', ...others);
  }

  lines.push(
    '',
    `The Markdown sources for these pages are in the repository at ${repositorySourcesUrl(version)}, and the library`,
    `itself is at ${repositoryUrl}.`,
  );

  return lines;
}

/** The `Start Here` list, with the curated slugs resolved against this version's pages. */
function startHereSection(source: DocsSource, version: DocsVersion): string[] {
  const lines = ['## Start Here', ''];

  for (const entry of curatedEntries(version)) {
    const page = source.getPage(entry.slug);

    if (page === undefined) {
      throw new Error(
        `The Start Here list in lib/llms.ts points at '${[versionBasePath(version), ...entry.slug].join('/')}', ` +
          `which is not a page in the ${version.label} documentation. Update curatedEntries for the move or removal.`,
      );
    }

    lines.push(`- [${page.data.title}](${siteUrl}${markdownHref(page.url)}): ${entry.note}`);
  }

  return lines;
}

/**
 * The `llms.txt` index for one version.
 *
 * @param version - The documentation line to index.
 */
export async function llmsIndex(version: DocsVersion): Promise<string> {
  const source = await getSource(version.id);
  const indexer = llms(source);
  const sections = sectionsFromTree(source.getPageTree(), version);
  const generated = sections.map((section) => {
    const body = section.nodes.map((node) => indexer.indexNode(node)).join('\n');

    return `## ${section.heading}\n\n${absoluteMarkdownLinks(body)}`;
  });

  return `${[
    ...preamble(version),
    '',
    ...startHereSection(source, version),
    '',
    ...generated.flatMap((section) => [section, '']),
  ]
    .join('\n')
    .trimEnd()}\n`;
}

/**
 * Every page of one version, in navigation order, with no page left out.
 *
 * Tree order first, then any page the tree does not reach. A page whose folder is missing from a `meta.json` is still
 * served by the site, so leaving it out here would make this file quietly incomplete.
 */
function orderedPages(source: DocsSource): DocsPage[] {
  const pages: DocsPage[] = [];
  const seen = new Set<string>();

  for (const item of walkPages(source.getPageTree().children)) {
    const page = source.getNodePage(item);

    if (page !== undefined && !seen.has(page.url)) {
      seen.add(page.url);
      pages.push(page);
    }
  }

  for (const page of source.getPages()) {
    if (!seen.has(page.url)) {
      seen.add(page.url);
      pages.push(page);
    }
  }

  return pages;
}

/**
 * The `llms-full.txt` document for one version: the complete text of every page.
 *
 * Page content is reproduced in full. Nothing is summarized, truncated, or filtered, including the snippet-source
 * comments, the roadmap, and the contributing guide. The only edit is to links, for the reason given on
 * `contentWithAbsoluteLinks`.
 *
 * @param version - The documentation line to render.
 */
export async function llmsFull(version: DocsVersion): Promise<string> {
  const source = await getSource(version.id);
  const pages = orderedPages(source);
  const documents = pages.map((page) => {
    const canonical = `${siteUrl}${page.url}`;
    const sourcePath = `site/${version.dir}/${page.path}`;

    return [
      `<!-- page: ${canonical} -->`,
      `<!-- markdown: ${siteUrl}${markdownHref(page.url)} -->`,
      `<!-- source: ${sourcePath} -->`,
      '',
      contentWithAbsoluteLinks(page, source, version).trim(),
      '',
    ].join('\n');
  });

  const contents = pages.map(
    (page) => `- [${page.data.title}](${siteUrl}${markdownHref(page.url)})`,
  );

  const header = [
    `# LiteBus ${version.label} Documentation, Complete Text`,
    '',
    `> The complete LiteBus ${version.label} documentation in one document: ${pages.length} pages in navigation`,
    `> order, describing ${releaseLine(version)}. For the index of the same pages with a note on each, read`,
    `> ${siteUrl}${llmsIndexPath(version)}.`,
    '',
    'Every page is reproduced in full. Nothing is summarized, truncated, or filtered out, including the roadmap, the',
    'contributing guide, the per-capability catalog pages, and the `snippet-source` comments that record which sample',
    'file a code block is compiled from. The one edit to page content is to links: a relative path to another Markdown',
    "source becomes the absolute URL of that page's `.md` endpoint, because a relative path resolves to nothing once",
    'the pages are concatenated. A link to Markdown the site does not serve becomes the file in the repository at the',
    'ref this version shipped from.',
    '',
    'Each page below is introduced by three HTML comments naming its page URL, its Markdown endpoint, and its source',
    'file in this repository. The page then begins at its own level-one heading, so heading levels within a page are',
    'the levels its author wrote. To split this document back into pages, split on lines beginning `<!-- page: `,',
    'which appear only as these delimiters. Do not split on `---`, because pages use horizontal rules of their own.',
    '',
    'Sources outside these pages, which this document does not contain:',
    '',
    `- Library source, issues, and releases: ${repositoryUrl}`,
    `- Markdown sources for this version: ${repositorySourcesUrl(version)}`,
    `- Release history for every version, including entries older than this line: ${repositoryUrl}/blob/${version.ref}/Changelog.md`,
    `- Repository conventions for contributors, which are not usage documentation: ${repositoryUrl}/blob/${version.ref}/AGENTS.md`,
    `- Compile-checked sample application: ${repositoryUrl}/tree/${version.ref}/samples/LiteBus.Sample`,
    `- Published packages: https://www.nuget.org/packages?q=LiteBus`,
    '',
    '## Contents',
    '',
    ...contents,
    '',
  ];

  return `${[...header, ...documents].join('\n').trimEnd()}\n`;
}

/**
 * One page's Markdown, as served by its `.md` endpoint.
 *
 * @param version - The documentation line the page belongs to.
 * @param slug - The page's slug, with the version prefix and the `docs` segment already removed.
 * @returns The page's Markdown, or `undefined` when the slug names no page.
 */
export async function pageMarkdown(
  version: DocsVersion,
  slug: string[],
): Promise<string | undefined> {
  const source = await getSource(version.id);
  const page = source.getPage(slug);

  if (page === undefined) {
    return undefined;
  }

  return `${contentWithAbsoluteLinks(page, source, version).trim()}\n`;
}
