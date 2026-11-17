// Renders one documentation page from Markdown to the HTML the site serves.
//
// The pipeline is: createMarkdownProcessor from @astrojs/markdown-remark with
// Shiki dual-theme highlighting and Mermaid fences compiled to inline SVG at
// build time, then post-processing: GitHub-style heading ids, the leading H1
// removed for the template, relative links rewritten to site URLs, tables
// given a scrolling wrapper, and code blocks wrapped in figures.
import { createMarkdownProcessor, type MarkdownRenderer, type RemarkPlugin } from "@astrojs/markdown-remark";
import { remarkOndine } from "ondine/remark";
import { decorateCodeBlocks } from "./code-blocks.ts";
import { resolveDocHref, type DocPage } from "./docs.ts";

/** The process-wide Markdown processor, created once. */
let processor: MarkdownRenderer | null = null;

/**
 * The shared Markdown processor.
 *
 * Shiki highlights every fenced block except Mermaid, which becomes a diagram before highlighting runs. Both themes
 * are emitted as CSS variables, and the stylesheet picks one per color scheme. Smart quotes stay off so code and
 * identifiers render exactly as written.
 */
async function renderer(): Promise<MarkdownRenderer> {
  if (!processor) {
    processor = await createMarkdownProcessor({
      syntaxHighlight: { type: "shiki", excludeLangs: ["mermaid"] },
      shikiConfig: { themes: { light: "github-light", dark: "github-dark" }, defaultColor: false, wrap: false },
      smartypants: false,
      // remarkOndine declares a narrower `this` type than the unified plugin type.
      remarkPlugins: [remarkOndine as unknown as RemarkPlugin],
    });
  }
  return processor;
}

/** One entry of the page table of contents. */
export interface TocEntry {
  /** The heading depth: 2 for sections, 3 for subsections. */
  depth: 2 | 3;
  /** The heading id the entry links to. */
  id: string;
  /** The heading text, without markup. */
  text: string;
}

/** A rendered documentation page. */
export interface RenderedPage {
  /** The page body HTML, without the leading H1 the template renders. */
  html: string;
  /** The H2 and H3 entries in document order. */
  toc: TocEntry[];
}

/** Tags stripped from a heading, leaving its text. */
function stripTags(value: string): string {
  return value.replace(/<[^>]*>/g, "");
}

/** HTML entities decoded back to the characters the heading holds. */
function unescapeHtml(value: string): string {
  return value
    .replace(/&#x27;/g, "'")
    .replace(/&#39;/g, "'")
    .replace(/&quot;/g, '"')
    .replace(/&gt;/g, ">")
    .replace(/&lt;/g, "<")
    .replace(/&amp;/g, "&");
}

/** Escapes a string for an HTML text node or a double-quoted attribute value. */
function escapeHtml(value: string): string {
  return value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

/**
 * A heading as a GitHub-style id: lower-cased, punctuation removed, spaces as dashes.
 *
 * Falls back to "section" when nothing id-worthy remains, so every heading still anchors.
 */
function githubSlug(text: string): string {
  const slug = text
    .toLowerCase()
    .replace(/['!"#$%&()*+,./:;<=>?@[\\\]^`{|}~’“”–—]/g, "")
    .replace(/\s/g, "-");
  return slug === "" ? "section" : slug;
}

/** A rendered heading: its depth and its inner HTML. */
const headingPattern = /<h([1-6])[^>]*>([\s\S]*?)<\/h\1>/g;

/** Gives rendered tables a scrolling wrapper, so wide tables scroll inside their own region on small screens. */
function styleTables(html: string): string {
  return html.replace(/<table>([\s\S]*?)<\/table>/g, (_match, inner: string) => {
    const scoped = inner
      .replace(/<thead>[\s\S]*?<\/thead>/, (head) =>
        head.replace(/<th(\s[^>]*)?>/g, (_m, attrs: string = "") => `<th scope="col"${attrs}>`),
      )
      .replace(/<tbody>[\s\S]*?<\/tbody>/, (body) =>
        body.replace(/<th(\s[^>]*)?>/g, (_m, attrs: string = "") => `<th scope="row"${attrs}>`),
      );
    const columns = [...inner.matchAll(/<th[^>]*>([\s\S]*?)<\/th>/g)]
      .map((m) => m[1].replace(/<[^>]+>/g, "").trim())
      .filter((name) => name !== "");
    const label = columns.length > 0 ? `Table with columns ${columns.join(", ")}` : "Table";
    const region = `<div class="table-wrap" role="region" tabindex="0" aria-label="${escapeHtml(label)}">`;
    return `${region}<table>${scoped}</table></div>`;
  });
}

/**
 * Rewrites the relative links in rendered HTML to site URLs.
 *
 * Fragment-only links stay as they are. Links that resolve outside the site, and links that were already absolute,
 * are external and gain `rel="noopener"`.
 */
function rewriteLinks(html: string, page: DocPage): string {
  return html.replace(/<a href="([^"]*)">/g, (match, raw: string) => {
    const href = unescapeHtml(raw);
    if (href === "" || href.startsWith("#")) {
      return match;
    }
    const resolved = resolveDocHref(href, page);
    if (resolved === href) {
      return /^https?:\/\//.test(href) ? `<a href="${raw}" rel="noopener">` : match;
    }
    const external = /^https?:\/\//.test(resolved) ? ` rel="noopener"` : "";
    return `<a href="${escapeHtml(resolved)}"${external}>`;
  });
}

/**
 * Renders one documentation page.
 *
 * The leading H1 is removed because the template renders the title itself; every other heading gains a GitHub-style
 * id and a section link, and the H2 and H3 headings form the table of contents.
 */
export async function renderPage(page: DocPage): Promise<RenderedPage> {
  const { code } = await (await renderer()).render(page.markdown);
  const used = new Map<string, number>();
  const toc: TocEntry[] = [];

  /** A heading id made unique within the page, with a numeric suffix from the second use on. */
  const unique = (id: string): string => {
    const seen = used.get(id) ?? 0;
    used.set(id, seen + 1);
    return seen === 0 ? id : `${id}-${seen + 1}`;
  };

  let firstH1 = true;
  let html = code.replace(headingPattern, (match, depth: string, inner: string) => {
    const text = unescapeHtml(stripTags(inner)).trim();
    const level = Number(depth);
    if (level === 1 && firstH1) {
      firstH1 = false;
      return "";
    }
    if (text === "") {
      return match;
    }
    const id = unique(githubSlug(text));
    const display = text.replace(/`/g, "").replace(/\s+/g, " ");
    if (level === 2) {
      toc.push({ depth: 2, id, text: display });
    } else if (level === 3) {
      toc.push({ depth: 3, id, text: display });
    }
    return (
      `<h${level} id="${escapeHtml(id)}">${inner}` +
      `<a class="anchor" href="#${escapeHtml(id)}" aria-label="Link to this section" data-pagefind-ignore>#</a></h${level}>`
    );
  });

  html = rewriteLinks(html, page);
  html = styleTables(html);
  html = decorateCodeBlocks(html);
  return { html, toc };
}

/**
 * Highlights one snippet to HTML, with the same classes as page code blocks.
 *
 * The home page shows registration snippets outside any documentation page; they run through the same processor and
 * the same figure wrapper so they read as one with the pages.
 */
export async function highlight(code: string, lang: string): Promise<string> {
  const { code: html } = await (await renderer()).render(`\`\`\`${lang}\n${code}\n\`\`\``);
  return decorateCodeBlocks(html);
}
