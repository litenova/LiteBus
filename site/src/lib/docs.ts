// Reads the documentation sources under content/docs and builds everything the
// site derives from them: the pages in reading order, the navigation tree in
// meta.json order, and the link resolution the rendered pages and the agent
// text share. Each Markdown file is read once, at import time.
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import {
  docsBasePath,
  docsContentDir,
  repositoryUrl,
  siteDescription,
  siteUrl,
  sourceRef,
} from "./site.ts";

/** A documentation page, with its source and its derived address and summary. */
export interface DocPage {
  /** POSIX path under content/docs, e.g. "getting-started/README.md". */
  file: string;
  /** URL segments: [] for index.md, ["getting-started"] for getting-started/README.md. */
  slug: string[];
  /** The page URL: "/docs" or "/docs/getting-started". */
  url: string;
  /** The page's Markdown URL: the page URL plus ".md". */
  mdUrl: string;
  /** The first H1 of the source. */
  title: string;
  /** The page's one-line description, from its prose or the site fallback. */
  description: string;
  /** The source text, CRLF normalised to LF. */
  markdown: string;
  /** The label of the top-level folder, or "Documentation" at the root. */
  section: string;
  /** ISO date (YYYY-MM-DD) of the last git commit touching the file, or the build date. */
  lastModified: string;
}

/** A node of the documentation navigation tree. */
export type NavNode =
  /** A group label from a "---Label---" entry in meta.json. */
  | { type: "separator"; label: string }
  /** A single page. */
  | { type: "page"; page: DocPage }
  /** A folder: its label, its index page when it has one, and its ordered children. */
  | { type: "folder"; label: string; index?: DocPage; children: NavNode[] };

/** The documentation content folder, from the working directory with a walk-up fallback. */
function findContentDir(): string {
  const direct = path.join(process.cwd(), docsContentDir);
  if (fs.existsSync(path.join(direct, "meta.json"))) {
    return direct;
  }
  for (let dir = process.cwd(); ; dir = path.dirname(dir)) {
    const candidate = path.join(dir, docsContentDir);
    if (fs.existsSync(path.join(candidate, "meta.json"))) {
      return candidate;
    }
    if (path.dirname(dir) === dir) {
      throw new Error(`No ${path.join(docsContentDir, "meta.json")} above ${process.cwd()}`);
    }
  }
}

/** The absolute path of the documentation content folder. */
const contentDir = findContentDir();

/** Every Markdown file under a folder, as absolute paths. */
function markdownFiles(dir: string): string[] {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      return markdownFiles(full);
    }
    return entry.name.toLowerCase().endsWith(".md") ? [full] : [];
  });
}

/** An absolute content path as a POSIX path under content/docs. */
function toContentPath(absolute: string): string {
  return path.relative(contentDir, absolute).split(path.sep).join("/");
}

/** True for a file stem that marks its folder's index page. */
function isIndexStem(stem: string): boolean {
  const lower = stem.toLowerCase();
  return lower === "readme" || lower === "index";
}

/** The stem of a content file without its extension. */
function stemOf(file: string): string {
  return path.posix.basename(file, path.posix.extname(file));
}

/** The content-relative folder of a file: "" at the root. */
function dirOf(file: string): string {
  const dir = path.posix.dirname(file);
  return dir === "." ? "" : dir;
}

/**
 * The URL slug of a content file: lower-case path segments without the
 * extension. A file named README.md or index.md, in any case, is its folder.
 */
function slugForFile(file: string): string[] {
  const dir = dirOf(file);
  const segments = dir === "" ? [] : dir.split("/").map((segment) => segment.toLowerCase());
  return isIndexStem(stemOf(file)) ? segments : [...segments, stemOf(file).toLowerCase()];
}

/** The page URL of a slug: the docs base path with the slug appended. */
function urlForSlug(slug: string[]): string {
  return slug.length === 0 ? docsBasePath : `${docsBasePath}/${slug.join("/")}`;
}

/** A file or folder name as a title: separators become spaces and each word is capitalized. */
function humanize(name: string): string {
  const titled = name
    .replace(/[-_]+/g, " ")
    .replace(/\b\w/g, (letter) => letter.toUpperCase())
    .trim();
  return titled === "" ? name : titled;
}

/**
 * Title and description derived from a page's Markdown.
 *
 * The documentation sources carry no frontmatter. Every page opens with an H1, and what follows it is one of three
 * shapes: an intro paragraph written for a reader, a metadata block with a `Summary` field, or a metadata block
 * followed by a `## Summary` section. Deriving the description from whichever shape a page uses keeps one copy of the
 * wording, so a page that is rewritten does not also have to have its frontmatter remembered.
 *
 * The description feeds the page's meta and Open Graph tags, the search index, and the note beside each entry in
 * `llms.txt`.
 */

/** Strips a byte-order mark, which the content root has at least one of. */
function withoutByteOrderMark(content: string): string {
  return content.charCodeAt(0) === 0xfeff ? content.slice(1) : content;
}

/** The text of the first level-one heading. */
function extractTitle(content: string): string | undefined {
  return /^#\s+(.+?)\s*$/m.exec(withoutByteOrderMark(content))?.[1];
}

/** Lines that open a block which is not prose, and so cannot contribute to a description. */
const nonProsePattern = /^(?:#{1,6}\s|```|~~~|\||>|[-*+]\s|\d+[.)]\s|<|\[!|:::)/;

/**
 * A metadata line, which the catalog and roadmap pages use to state a capability's identifier and maturity.
 *
 * Written either as a list item (`- **Summary**: text`) or as a bare line (`**Maturity:** GA`). The label is capped in
 * length so that an ordinary sentence opening with a bold phrase is not mistaken for a field.
 */
const metadataLinePattern = /^-?\s*\*\*([A-Za-z][^*]{0,48}?)\*\*\s*:?\s*(.*)$/;

/** The heading whose section holds the description on a page that opens with a metadata block. */
const summaryHeadingPattern = /^##\s+summary\s*$/i;

/** Any heading, used to bound the intro block and each section. */
const headingPattern = /^#{1,6}\s/;

/** The metadata field whose value is already written as a one-line description. */
const summaryFieldLabel = "summary";

/**
 * Inline Markdown removed from a description, in application order.
 *
 * A description is plain text in a meta tag and in a `llms.txt` list item, so link syntax and emphasis markers have to
 * come out while the words they wrap stay.
 */
const inlineReplacements: readonly [RegExp, string][] = [
  [/!\[[^\]]*\]\([^)]*\)/g, ""],
  [/\[([^\]]*)\]\([^)]*\)/g, "$1"],
  [/`([^`]*)`/g, "$1"],
  [/\*\*([^*]+)\*\*/g, "$1"],
  [/__([^_]+)__/g, "$1"],
  [/(?<![\w*])\*([^*]+)\*(?![\w*])/g, "$1"],
  [/<[^>]+>/g, ""],
  [/\s+/g, " "],
];

/** Longest description emitted before it is cut at a word boundary. */
const descriptionLimit = 240;

/**
 * A one-line description of a page.
 *
 * The strategies are tried in the order that yields the most specific wording: the intro paragraph a reader would read
 * first, then an explicit `Summary` field, then the `## Summary` section, then any prose the page has at all. A page
 * with none of those, such as one whose body is entirely a table, gets no description rather than a misleading one.
 */
function extractDescription(content: string): string | undefined {
  const lines = withoutByteOrderMark(content).split(/\r?\n/);
  const headingIndex = lines.findIndex((line) => /^#\s+/.test(line));
  const body = lines.slice(headingIndex + 1);
  const intro = untilHeading(body);
  const text =
    firstProse(intro) ?? summaryField(intro) ?? firstProse(summarySection(body)) ?? firstProse(body);

  return text === undefined ? undefined : clampToSentence(text);
}

/** The lines of a block that come before its first heading. */
function untilHeading(lines: readonly string[]): string[] {
  const end = lines.findIndex((line) => headingPattern.test(line.trim()));

  return end === -1 ? [...lines] : lines.slice(0, end);
}

/**
 * The first prose paragraph in a block of lines, as plain text.
 *
 * Blocks that are not prose are skipped while looking for the paragraph and end it once one has started, so a
 * paragraph is never joined to the table or list that follows it.
 */
function firstProse(lines: readonly string[]): string | undefined {
  const paragraph: string[] = [];

  for (const line of lines) {
    const trimmed = line.trim();

    if (trimmed.length === 0 || nonProsePattern.test(trimmed) || metadataLinePattern.test(trimmed)) {
      if (paragraph.length > 0) {
        break;
      }

      continue;
    }

    paragraph.push(trimmed);
  }

  return paragraph.length === 0 ? undefined : clean(paragraph.join(" "));
}

/** The value of the `Summary` metadata field, when the block declares one. */
function summaryField(lines: readonly string[]): string | undefined {
  for (const line of lines) {
    const field = metadataLinePattern.exec(line.trim());

    if (field?.[1].trim().toLowerCase() === summaryFieldLabel && field[2].trim().length > 0) {
      return clean(field[2]);
    }
  }

  return undefined;
}

/** The lines under a `## Summary` heading, up to the next heading. */
function summarySection(body: readonly string[]): string[] {
  const start = body.findIndex((line) => summaryHeadingPattern.test(line.trim()));

  if (start === -1) {
    return [];
  }

  return untilHeading(body.slice(start + 1));
}

/** Markdown reduced to the plain text a description is. */
function clean(text: string): string | undefined {
  const plain = inlineReplacements
    .reduce((carry, [pattern, replacement]) => carry.replace(pattern, replacement), text)
    .trim();

  return plain.length === 0 ? undefined : plain;
}

/**
 * The leading sentence, or a word-boundary cut when there is no sentence break within the limit.
 *
 * A terminator only ends a sentence when the character before it is not a space and the character after it is. That
 * leaves `.NET`, `6.0.2`, and a trailing `*.Extensions.Microsoft.DependencyInjection` intact, which a plainer split on
 * `.` would not.
 */
function clampToSentence(text: string): string {
  const sentence = /[^\s]([.!?])(?=\s|$)/.exec(text);

  if (sentence?.index !== undefined) {
    const end = sentence.index + sentence[0].length;

    if (end <= descriptionLimit) {
      return text.slice(0, end);
    }
  }

  if (text.length <= descriptionLimit) {
    return text;
  }

  const cut = text.lastIndexOf(" ", descriptionLimit);

  return `${text.slice(0, cut === -1 ? descriptionLimit : cut)}...`;
}

/** Today's date as YYYY-MM-DD, used when git cannot date a file. */
const buildDate = new Date().toISOString().slice(0, 10);

/**
 * The last-commit date of every tracked file under the content folder.
 *
 * One `git log` pass over the whole folder, parsed into a map: the log lists commits newest first, so the first date
 * seen for a file is its last change. Empty when git is unavailable, and then every page takes the build date.
 */
function loadLastModified(): Map<string, string> {
  const dates = new Map<string, string>();
  try {
    const prefix = execFileSync("git", ["rev-parse", "--show-prefix"], {
      cwd: contentDir,
      encoding: "utf8",
    }).trim();
    const output = execFileSync("git", ["log", "--name-only", "--format=%cs", "--", "."], {
      cwd: contentDir,
      encoding: "utf8",
      maxBuffer: 16 * 1024 * 1024,
    });
    let date = "";
    for (const line of output.split("\n")) {
      const trimmed = line.trim();
      if (trimmed === "") {
        continue;
      }
      if (/^\d{4}-\d{2}-\d{2}$/.test(trimmed)) {
        date = trimmed;
        continue;
      }
      const relative = trimmed.startsWith(prefix) ? trimmed.slice(prefix.length) : trimmed;
      if (date !== "" && !dates.has(relative)) {
        dates.set(relative, date);
      }
    }
  } catch {
    // Without git every page falls back to the build date below.
  }
  return dates;
}

/** The last-commit dates by content-relative path, from one git log pass. */
const lastModifiedByFile = loadLastModified();

/** A folder's meta.json: its display title and its ordered child names. */
interface FolderMeta {
  /** The display title, when the file states one. */
  title?: string;
  /** The child file or folder names in order; "---Label---" entries are separators. */
  pages: string[];
}

/** The parsed meta.json files, keyed by content-relative folder ("" at the root). */
const metas = new Map<string, FolderMeta>();

/** The meta.json of a content folder, or an empty order when it has none. */
function readMeta(dirRel: string): FolderMeta {
  const cached = metas.get(dirRel);
  if (cached !== undefined) {
    return cached;
  }
  const meta: FolderMeta = { pages: [] };
  try {
    const raw = JSON.parse(fs.readFileSync(path.join(contentDir, dirRel, "meta.json"), "utf8")) as {
      title?: unknown;
      pages?: unknown;
    };
    if (typeof raw.title === "string" && raw.title.length > 0) {
      meta.title = raw.title;
    }
    if (Array.isArray(raw.pages)) {
      meta.pages = raw.pages.filter((entry): entry is string => typeof entry === "string");
    }
  } catch {
    // A folder without meta.json keeps its title and order unset.
  }
  metas.set(dirRel, meta);
  return meta;
}

/** One Markdown file as a documentation page. */
function toPage(file: string, markdown: string): DocPage {
  const slug = slugForFile(file);
  const url = urlForSlug(slug);
  const index = isIndexStem(stemOf(file));
  const indexName =
    dirOf(file) === "" ? "Documentation" : humanize(dirOf(file).split("/").pop() ?? file);
  const title = extractTitle(markdown) ?? (index ? indexName : humanize(stemOf(file)));
  return {
    file,
    slug,
    url,
    mdUrl: `${url}.md`,
    title,
    description: extractDescription(markdown) ?? siteDescription,
    markdown,
    section: sectionForFile(file),
    lastModified: lastModifiedByFile.get(file) ?? buildDate,
  };
}

/** The section label of a page: its top-level folder's title, or the root title at the root. */
function sectionForFile(file: string): string {
  const top = file.split("/")[0];
  if (file.includes("/") && top !== undefined) {
    return readMeta(top).title ?? humanize(top);
  }
  return readMeta("").title ?? "Documentation";
}

/** Every documentation page, keyed by content-relative file path. */
const pagesByFile = new Map<string, DocPage>(
  markdownFiles(contentDir)
    .sort()
    .map((absolute) => {
      const file = toContentPath(absolute);
      const markdown = fs.readFileSync(absolute, "utf8").replace(/\r\n/g, "\n");
      return [file, toPage(file, markdown)] as const;
    }),
);

/** Each folder's index page, keyed by content-relative folder. */
const indexByDir = new Map<string, DocPage>();

/** Each folder's direct non-index pages, keyed by content-relative folder. */
const pagesByDir = new Map<string, DocPage[]>();

/** Each folder's immediate subfolders that hold pages, keyed by content-relative folder. */
const subdirsByDir = new Map<string, string[]>();

/** Groups the pages into their folders and links each folder to its parent. */
function groupPagesByFolder(): void {
  const dirs = new Set<string>([""]);
  for (const page of pagesByFile.values()) {
    const dir = dirOf(page.file);
    // Add every ancestor too, so a folder that holds only subfolders (such as catalog/) still appears.
    for (let ancestor = dir; ancestor !== ""; ancestor = ancestor.includes("/") ? ancestor.slice(0, ancestor.lastIndexOf("/")) : "") {
      dirs.add(ancestor);
    }
    if (isIndexStem(stemOf(page.file))) {
      indexByDir.set(dir, page);
    } else {
      const siblings = pagesByDir.get(dir) ?? [];
      siblings.push(page);
      pagesByDir.set(dir, siblings);
    }
  }
  for (const dir of dirs) {
    if (dir === "") {
      continue;
    }
    const parent = dir.includes("/") ? dir.slice(0, dir.lastIndexOf("/")) : "";
    const siblings = subdirsByDir.get(parent) ?? [];
    siblings.push(dir);
    subdirsByDir.set(parent, siblings);
  }
}

groupPagesByFolder();

/** The label of a folder: its meta.json title, its index title, or its name. */
function folderLabel(dirRel: string): string {
  return (
    readMeta(dirRel).title ?? indexByDir.get(dirRel)?.title ?? humanize(dirRel.split("/").pop() ?? dirRel)
  );
}

/** A "---Label---" separator entry, with the label. */
const separatorPattern = /^---(.+)---$/;

/** A folder node with its children in meta.json order. */
function folderNode(dirRel: string): NavNode {
  const index = indexByDir.get(dirRel);
  const children = folderChildren(dirRel, false);
  return index === undefined
    ? { type: "folder", label: folderLabel(dirRel), children }
    : { type: "folder", label: folderLabel(dirRel), index, children };
}

/**
 * A folder's children in meta.json order.
 *
 * The `pages` array lists child file or folder names; a "---Label---" entry is a separator. A child missing from the
 * array is appended after the listed ones, sorted by title. The root's own index page stays a child; any other
 * folder's index leads the folder instead, so its name in the array is skipped.
 */
function folderChildren(dirRel: string, isRoot: boolean): NavNode[] {
  const nodes: NavNode[] = [];
  const direct = pagesByDir.get(dirRel) ?? [];
  const rootIndex = isRoot ? indexByDir.get("") : undefined;
  const candidates = rootIndex === undefined ? direct : [...direct, rootIndex];
  const subdirs = subdirsByDir.get(dirRel) ?? [];
  const pageByName = new Map(candidates.map((page) => [stemOf(page.file).toLowerCase(), page]));
  const subdirByName = new Map(
    subdirs.map((sub) => [sub.split("/").pop()?.toLowerCase() ?? sub, sub]),
  );
  const usedPages = new Set<string>();
  const usedSubdirs = new Set<string>();
  for (const entry of readMeta(dirRel).pages) {
    const separator = separatorPattern.exec(entry);
    if (separator !== null) {
      nodes.push({ type: "separator", label: separator[1] });
      continue;
    }
    const sub = subdirByName.get(entry.toLowerCase());
    if (sub !== undefined) {
      usedSubdirs.add(sub);
      nodes.push(folderNode(sub));
      continue;
    }
    const page = pageByName.get(entry.toLowerCase());
    if (page !== undefined) {
      usedPages.add(page.file);
      nodes.push({ type: "page", page });
      continue;
    }
    if (!isRoot && isIndexStem(entry)) {
      continue;
    }
  }
  const extra: { title: string; node: NavNode }[] = [];
  for (const page of candidates) {
    if (!usedPages.has(page.file)) {
      extra.push({ title: page.title, node: { type: "page", page } });
    }
  }
  for (const sub of subdirs) {
    if (!usedSubdirs.has(sub)) {
      extra.push({ title: folderLabel(sub), node: folderNode(sub) });
    }
  }
  extra.sort((a, b) => (a.title < b.title ? -1 : a.title > b.title ? 1 : 0));
  return [...nodes, ...extra.map((entry) => entry.node)];
}

/** The navigation tree: the root meta.json order, folders ordered by their own meta.json. */
export const navTree: NavNode[] = folderChildren("", true);

/** Collects the pages under navigation nodes, depth first, a folder's index ahead of its children. */
function collectPages(nodes: NavNode[], out: DocPage[]): void {
  for (const node of nodes) {
    if (node.type === "page") {
      out.push(node.page);
    } else if (node.type === "folder") {
      if (node.index !== undefined) {
        out.push(node.index);
      }
      collectPages(node.children, out);
    }
  }
}

/** Every page, in reading order (nav tree order, depth first). */
export const pages: DocPage[] = (() => {
  const ordered: DocPage[] = [];
  collectPages(navTree, ordered);
  const seen = new Set(ordered.map((page) => page.file));
  const rest = [...pagesByFile.values()]
    .filter((page) => !seen.has(page.file))
    .sort((a, b) => (a.file < b.file ? -1 : a.file > b.file ? 1 : 0));
  return [...ordered, ...rest];
})();

/** Every page, keyed by its slug joined with "/". */
const pagesBySlug = new Map(pages.map((page) => [page.slug.join("/"), page]));

/** The page for a URL slug, or undefined when no page has it. */
export function getPage(slug: string[]): DocPage | undefined {
  return pagesBySlug.get(slug.join("/"));
}

/** The pages around a page in reading order. */
export function pageNeighbors(page: DocPage): { prev?: DocPage; next?: DocPage } {
  const at = pages.findIndex((entry) => entry.file === page.file);
  return {
    prev: at > 0 ? pages[at - 1] : undefined,
    next: at >= 0 && at < pages.length - 1 ? pages[at + 1] : undefined,
  };
}

/**
 * The breadcrumb trail of a page: "Docs" first, the current page last without a URL.
 *
 * Each folder between them links to its index page; a folder without an index page carries its label only.
 */
export function breadcrumbs(page: DocPage): { label: string; url?: string }[] {
  if (page.slug.length === 0) {
    return [{ label: "Docs" }];
  }
  const crumbs: { label: string; url?: string }[] = [{ label: "Docs", url: docsBasePath }];
  const dir = dirOf(page.file);
  const segments = dir === "" ? [] : dir.split("/");
  const index = isIndexStem(stemOf(page.file));
  segments.forEach((_segment, at) => {
    const dirRel = segments.slice(0, at + 1).join("/");
    const label = folderLabel(dirRel);
    const url = at === segments.length - 1 && index ? undefined : indexByDir.get(dirRel)?.url;
    crumbs.push(url === undefined ? { label } : { label, url });
  });
  if (!index) {
    crumbs.push({ label: page.title });
  }
  return crumbs;
}

/** The reading time of a page in minutes, at 220 words a minute, at least one. */
export function readingTime(page: DocPage): number {
  return Math.max(1, Math.round(page.markdown.split(/\s+/).length / 220));
}

/** Matches a link that points at a Markdown source file, with or without a fragment. */
const markdownHrefPattern = /\.md(?:#|$)/i;

/** Matches a link that already names a scheme, including `mailto:` and protocol-relative forms. */
const absoluteHrefPattern = /^(?:[a-z][a-z0-9+.-]*:|\/\/)/i;

/** Where the Markdown sources are browsable, used for links that resolve to no page on the site. */
function repositorySourcesUrl(): string {
  return `${repositoryUrl}/blob/${sourceRef}/site/${docsContentDir}/`;
}

/**
 * The URL a link in a documentation page points at.
 *
 * Pages link to each other by source path (`../getting-started/README.md`) so that the Markdown stays readable in the
 * repository. A link to a page the site serves becomes that page's path, with any `#hash` kept. A link to Markdown
 * the site does not serve falls back to the sources in the repository at the ref the documentation is built from.
 * Anything that is not a relative Markdown path is returned untouched.
 */
export function resolveDocHref(href: string, page: DocPage): string {
  if (href === "" || !markdownHrefPattern.test(href) || absoluteHrefPattern.test(href)) {
    return href;
  }
  // Bare sibling names such as `capability-catalog.md` resolve like explicit relative paths.
  const sourceHref = href.startsWith(".") ? href : `./${href}`;
  const fragmentAt = sourceHref.indexOf("#");
  const target = fragmentAt < 0 ? sourceHref : sourceHref.slice(0, fragmentAt);
  const fragment = fragmentAt < 0 ? "" : sourceHref.slice(fragmentAt);
  const resolved = path.posix.normalize(path.posix.join(dirOf(page.file) || ".", target));
  const found = pagesByFile.get(resolved);
  if (found !== undefined) {
    return `${found.url}${fragment}`;
  }
  return new URL(href, new URL(page.file, repositorySourcesUrl())).toString();
}

/** The `.md` endpoint for a documentation path, keeping any fragment after the extension. */
function markdownHref(href: string): string {
  const fragmentAt = href.indexOf("#");
  const pathPart = fragmentAt < 0 ? href : href.slice(0, fragmentAt);
  const fragment = fragmentAt < 0 ? "" : href.slice(fragmentAt);
  return `${pathPart}.md${fragment}`;
}

/**
 * A link in a page as an absolute Markdown URL.
 *
 * Site paths become absolute `.md` endpoints on the site, so agents that follow a link out of generated text land on
 * Markdown. Anything already absolute, such as a repository fallback or an external link, is returned untouched.
 */
export function absoluteMarkdownHref(href: string, page: DocPage): string {
  const resolved = resolveDocHref(href, page);
  return resolved.startsWith("/") ? `${siteUrl}${markdownHref(resolved)}` : resolved;
}
