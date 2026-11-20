// The plain-text views of the documentation that agents read.
//
// Three endpoints, following the llms.txt convention (https://llmstxt.org):
//
// - `llms.txt` is an index: a short summary of the library, then one link per page with a note.
// - `llms-full.txt` is every page's complete Markdown in one document.
// - `<page>.md` is one page's Markdown, which is what the links in the two files above point at.
//
// The index is part hand-written and part generated. The summary, the orientation prose, and the `Start Here` list
// are written here because a client arriving with no context needs the shape of the library before it needs a file
// listing. Everything below `Start Here` comes from the same page tree the navigation uses, so a page added to
// `meta.json` appears here without anyone remembering to add it.
import {
  absoluteMarkdownHref,
  getPage,
  navTree,
  pages,
  type DocPage,
  type NavNode,
} from "./docs.ts";
import {
  docsBasePath,
  docsContentDir,
  latestRelease,
  repositoryUrl,
  siteUrl,
  sourceRef,
} from "./site.ts";

// Sections placed under the `Optional` heading.
//
// The convention reserves that heading for pages a client can skip when its context is tight. The roadmap describes
// work that has not shipped and the contributing guide describes this repository's process, so neither answers a
// question about how to use the library.
const optionalSections = ["roadmap", "contributing"] as const;

/** A section of the index: an H2 heading and the page tree nodes listed under it. */
type IndexSection = {
  heading: string;
  nodes: NavNode[];
};

/** A hand-written entry in `Start Here`. */
type CuratedEntry = {
  slug: string[];
  note: string;
};

// The pages to read first, in order, with the reason to read each.
//
// These notes describe what a page is for rather than what it says, so they stay true as the pages are edited. Every
// slug is resolved against the pages being rendered and a missing one fails the build, which is what keeps the list
// from quietly rotting as pages move.
function curatedEntries(): CuratedEntry[] {
  return [
    {
      slug: [],
      note: "Documentation index. The map of every area, and the table of which page answers which goal.",
    },
    {
      slug: ["getting-started"],
      note: "Install the packages, register the modules, write a first handler, and send a first message.",
    },
    {
      slug: ["reference", "feature-index"],
      note: "Capability to documentation to package, one table per axis. The fastest way to find the package a feature needs.",
    },
    {
      slug: ["reference", "capability-catalog"],
      note: "Every shipped and planned capability with a stable identifier, maturity tier, package mapping, and deep link.",
    },
    {
      slug: ["architecture", "dependency-graph"],
      note: "Which packages to install for a given combination of modules, container, storage, and broker.",
    },
    {
      slug: ["getting-started", "cheat-sheet"],
      note: "Contract interfaces, handler interfaces, builder calls, and mediator signatures in one place.",
    },
    {
      slug: ["reference", "glossary"],
      note: "The exact meaning of the terms these pages use, including the ones that differ from other messaging libraries.",
    },
    {
      slug: ["migration", "mediatr-differences"],
      note: "The MediatR equivalences, for translating an existing application rather than starting a new one.",
    },
  ];
}

/** The documentation path a node leads to, used to decide which section it belongs in. */
function nodeUrl(node: NavNode): string | undefined {
  if (node.type === "page") {
    return node.page.url;
  }

  if (node.type === "folder") {
    return node.index?.url ?? firstPage(node.children)?.url;
  }

  return undefined;
}

/** The first page reachable under a list of nodes, in tree order. */
function firstPage(nodes: readonly NavNode[]): DocPage | undefined {
  for (const page of walkPages(nodes)) {
    return page;
  }

  return undefined;
}

// Every page under a list of nodes, in the order the navigation shows them.
//
// A folder's index page is hoisted onto the folder, so it is emitted ahead of the folder's other children rather than
// being left out. Callers dedupe on URL: whether an index page also stays in `children` is a page tree detail this
// does not need to depend on.
function* walkPages(nodes: readonly NavNode[]): Generator<DocPage> {
  for (const node of nodes) {
    if (node.type === "page") {
      yield node.page;
    } else if (node.type === "folder") {
      if (node.index !== undefined) {
        yield node.index;
      }

      yield* walkPages(node.children);
    }
  }
}

// The index's sections, taken from the separators in `meta.json`.
//
// The separators already divide the navigation into the groups a reader is offered, and the convention's H2 sections
// are the same idea, so one drives the other. A node ahead of the first separator is dropped, which means the
// documentation index page: `Start Here` links it with a better note than its own description makes.
function sectionsFromTree(tree: readonly NavNode[]): IndexSection[] {
  const base = docsBasePath;
  const optionalPaths = optionalSections.map((section) => `${base}/${section}`);
  const sections: IndexSection[] = [];
  const optional: IndexSection = { heading: "Optional", nodes: [] };
  let current: IndexSection | undefined;

  for (const child of tree) {
    if (child.type === "separator") {
      current = { heading: child.label, nodes: [] };
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

/** Link text with brackets escaped, so one entry stays one link. */
function escapeLinkText(text: string): string {
  return text.replace(/([[\]])/g, "\\$1");
}

// One index line per page under a list of nodes, in navigation order.
//
// The entry links to the page's `.md` endpoint rather than to the page, so a client that follows a link gets Markdown
// instead of an application shell. The note after the link is the page's description, which is never empty. Folder
// structure is not rendered as its own rows: every entry is a page with a note.
function indexLines(nodes: readonly NavNode[]): string[] {
  const lines: string[] = [];
  const seen = new Set<string>();

  for (const page of walkPages(nodes)) {
    if (seen.has(page.url)) {
      continue;
    }

    seen.add(page.url);
    lines.push(`- [${escapeLinkText(page.title)}](${siteUrl}${page.mdUrl}): ${page.description}`);
  }

  return lines;
}

/** Strips the byte-order mark that at least one source file carries, so output starts at its first heading. */
function withoutByteOrderMark(content: string): string {
  return content.charCodeAt(0) === 0xfeff ? content.slice(1) : content;
}

/** Where the Markdown sources are browsable, used for links that resolve to no page on the site. */
function repositorySourcesUrl(): string {
  return `${repositoryUrl}/blob/${sourceRef}/site/${docsContentDir}/`;
}

// A page's Markdown with every link usable from outside the site.
//
// The sources link to each other by relative source path, which resolves to nothing once a page is served on its own
// or concatenated with every other page. Each one becomes the absolute URL of the target's `.md` endpoint, or the
// file in the repository when the target is Markdown the site does not serve.
function contentWithAbsoluteLinks(page: DocPage): string {
  return withoutByteOrderMark(page.markdown).replace(
    /\]\(([^)]+)\)/g,
    (match, href: string) => {
      // A link with a title (`](url "title")`) would need splitting before resolution. None of the sources use one,
      // so such a link is left exactly as written rather than guessed at.
      if (/\s/.test(href)) {
        return match;
      }

      return `](${absoluteMarkdownHref(href, page)})`;
    },
  );
}

// Where the index is served.
//
// The index answers at the site root, which is the path the convention has clients probe first. `public/_redirects`
// sends `/docs/llms.txt` to this route for clients that guess the prefixed form.
export const llmsIndexPath = "/llms.txt";

/** Where the complete text is served, following `llmsIndexPath`. */
export const llmsFullPath = "/llms-full.txt";

/** How the documentation describes the release it documents, for the lines that have to name it. */
function releaseLine(): string {
  return `LiteBus ${latestRelease}`;
}

// The summary and orientation prose above the file lists.
//
// The convention allows any Markdown except headings between the summary blockquote and the first H2, and this is what
// goes there: what the library is, where the durability boundary sits, how the packages are shaped, and what the rest
// of this file set contains. A client that reads only this far should still be able to choose a package.
function preamble(): string[] {
  return [
    "# LiteBus",
    "",
    "> LiteBus is a mediator and durable messaging library for .NET 10 and .NET 11, MIT licensed. It",
    "> provides separate command, query, and event contracts, each with its own mediator, handlers, and pipeline, and",
    "> adds opt-in inbox, outbox, saga, storage, transport, ingress, hosting, and observability modules that persist",
    "> and carry those messages between processes.",
    "",
    `This index describes ${releaseLine()}. Every link below points at Markdown rather than at a rendered page:`,
    `appending \`.md\` to any documentation URL returns that page's source. The complete text of every page listed here`,
    `is available as a single document at ${siteUrl}${llmsFullPath}.`,
    "",
    "Mediation and durability are separate decisions. Command, query, and event mediation executes handlers in the",
    "caller's process and persists nothing. Durable behavior begins only when an application selects an inbox or",
    "outbox module together with a storage adapter and a processor host; until then no message is written anywhere.",
    "Broker and database integrations ship as separate packages, so an application installs only the SDKs it uses.",
    "",
    "The packages are granular for that reason: one package per module per dependency injection container, and one per",
    "storage or broker adapter. `LiteBus.Commands.Extensions.Microsoft.DependencyInjection` is the command module for",
    "Microsoft dependency injection, and queries, events, and Autofac follow the same shape. The feature index and the",
    "dependency graph linked below map a capability to the packages that provide it.",
    "",
    "Registration composes the modules an application installed:",
    "",
    "```csharp",
    "services.AddLiteBus(liteBus =>",
    "{",
    "    var applicationAssembly = typeof(ProcessPaymentCommand).Assembly;",
    "",
    "    liteBus.AddMessaging(_ =>",
    "    {",
    "    });",
    "",
    "    liteBus.AddCommands(commands => commands.RegisterFromAssembly(applicationAssembly));",
    "    liteBus.AddQueries(queries => queries.RegisterFromAssembly(applicationAssembly));",
    "    liteBus.AddEvents(events => events.RegisterFromAssembly(applicationAssembly));",
    "});",
    "```",
    "",
    `The Markdown sources for these pages are in the repository at ${repositorySourcesUrl()}, and the library`,
    `itself is at ${repositoryUrl}.`,
  ];
}

/** The `Start Here` list, with the curated slugs resolved against the pages. */
function startHereSection(): string[] {
  const lines = ["## Start Here", ""];

  for (const entry of curatedEntries()) {
    const page = getPage(entry.slug);

    if (page === undefined) {
      throw new Error(
        `The Start Here list in src/lib/llms.ts points at '${[docsBasePath, ...entry.slug].join("/")}', ` +
          `which is not a page in the documentation. Update curatedEntries for the move or removal.`,
      );
    }

    lines.push(`- [${page.title}](${siteUrl}${page.mdUrl}): ${entry.note}`);
  }

  return lines;
}

/** The `llms.txt` index. */
export function llmsIndex(): string {
  const sections = sectionsFromTree(navTree);
  const generated = sections.map((section) => {
    const body = indexLines(section.nodes).join("\n");

    return `## ${section.heading}\n\n${body}`;
  });

  return `${[
    ...preamble(),
    "",
    ...startHereSection(),
    "",
    ...generated.flatMap((section) => [section, ""]),
  ]
    .join("\n")
    .trimEnd()}\n`;
}

// Every page, in navigation order, with no page left out.
//
// Tree order first, then any page the tree does not reach. A page whose folder is missing from a `meta.json` is still
// served by the site, so leaving it out here would make this file quietly incomplete.
function orderedPages(): DocPage[] {
  const ordered: DocPage[] = [];
  const seen = new Set<string>();

  for (const page of walkPages(navTree)) {
    if (!seen.has(page.url)) {
      seen.add(page.url);
      ordered.push(page);
    }
  }

  for (const page of pages) {
    if (!seen.has(page.url)) {
      seen.add(page.url);
      ordered.push(page);
    }
  }

  return ordered;
}

// The `llms-full.txt` document: the complete text of every page.
//
// Page content is reproduced in full. Nothing is summarized, truncated, or filtered, including the snippet-source
// comments, the roadmap, and the contributing guide. The only edit is to links, for the reason given on
// `contentWithAbsoluteLinks`.
export function llmsFull(): string {
  const ordered = orderedPages();
  const documents = ordered.map((page) => {
    const canonical = `${siteUrl}${page.url}`;
    const sourcePath = `site/${docsContentDir}/${page.file}`;

    return [
      `<!-- page: ${canonical} -->`,
      `<!-- markdown: ${siteUrl}${page.mdUrl} -->`,
      `<!-- source: ${sourcePath} -->`,
      "",
      contentWithAbsoluteLinks(page).trim(),
      "",
    ].join("\n");
  });

  const contents = ordered.map(
    (page) => `- [${page.title}](${siteUrl}${page.mdUrl})`,
  );

  const header = [
    "# LiteBus Documentation, Complete Text",
    "",
    `> The complete LiteBus documentation in one document: ${ordered.length} pages in navigation`,
    `> order, describing ${releaseLine()}. For the index of the same pages with a note on each, read`,
    `> ${siteUrl}${llmsIndexPath}.`,
    "",
    "Every page is reproduced in full. Nothing is summarized, truncated, or filtered out, including the roadmap, the",
    "contributing guide, the per-capability catalog pages, and the `snippet-source` comments that record which sample",
    "file a code block is compiled from. The one edit to page content is to links: a relative path to another Markdown",
    "source becomes the absolute URL of that page's `.md` endpoint, because a relative path resolves to nothing once",
    "the pages are concatenated. A link to Markdown the site does not serve becomes the file in the repository at the",
    "ref the documentation sources live on.",
    "",
    "Each page below is introduced by three HTML comments naming its page URL, its Markdown endpoint, and its source",
    "file in this repository. The page then begins at its own level-one heading, so heading levels within a page are",
    "the levels its author wrote. To split this document back into pages, split on lines beginning `<!-- page: `,",
    "which appear only as these delimiters. Do not split on `---`, because pages use horizontal rules of their own.",
    "",
    "Sources outside these pages, which this document does not contain:",
    "",
    `- Library source, issues, and releases: ${repositoryUrl}`,
    `- Markdown sources for these pages: ${repositorySourcesUrl()}`,
    `- Release history: ${repositoryUrl}/blob/${sourceRef}/Changelog.md`,
    `- Repository conventions for contributors, which are not usage documentation: ${repositoryUrl}/blob/${sourceRef}/AGENTS.md`,
    `- Compile-checked sample application: ${repositoryUrl}/tree/${sourceRef}/samples/LiteBus.Sample`,
    `- Published packages: https://www.nuget.org/packages?q=LiteBus`,
    "",
    "## Contents",
    "",
    ...contents,
    "",
  ];

  return `${[...header, ...documents].join("\n").trimEnd()}\n`;
}

// One page's Markdown, as served by its `.md` endpoint.
//
// The one-line header names the page's canonical HTML URL, so a client holding the Markdown alone still knows which
// rendered page it came from.
//
// @param slug - The page's slug, with the `docs` segment already removed.
// @returns The page's Markdown, or `undefined` when the slug names no page.
export function pageMarkdown(slug: string[]): string | undefined {
  const page = getPage(slug);

  if (page === undefined) {
    return undefined;
  }

  const canonical = `${siteUrl}${page.url}`;

  return `<!-- canonical: ${canonical} -->\n\n${contentWithAbsoluteLinks(page).trim()}\n`;
}
