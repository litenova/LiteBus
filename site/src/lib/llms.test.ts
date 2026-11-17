// Tests for the plain-text views of the documentation: the index shape, the
// complete text, and one page's Markdown.
import { describe, it } from "node:test";
import assert from "node:assert";
import { llmsFull, llmsIndex, pageMarkdown } from "./llms.ts";
import { getPage, navTree, pages } from "./docs.ts";
import { siteUrl } from "./site.ts";

/** Every Markdown link in a document, as `{ text, url }`. */
function markdownLinks(text: string): { text: string; url: string }[] {
  return [...text.matchAll(/\[([^\]]*)\]\(([^)]+)\)/g)].map((match) => ({
    text: match[1],
    url: match[2],
  }));
}

describe("llmsIndex", () => {
  it("opens with one H1, a blockquote summary, and H2 sections only", () => {
    const index = llmsIndex();
    const lines = index.split("\n");
    const firstContent = lines.find((line) => line.trim().length > 0);
    assert.ok(firstContent?.startsWith("# "));
    const headings = lines.filter((line) => /^#{1,6}\s/.test(line));
    assert.strictEqual(headings.filter((heading) => heading.startsWith("# ")).length, 1);
    const firstSection = lines.findIndex((line) => line.startsWith("## "));
    assert.notStrictEqual(firstSection, -1);
    assert.ok(lines.slice(0, firstSection).some((line) => line.startsWith("> ")));
    assert.strictEqual(headings.find((heading) => /^#{3,6}\s/.test(heading)), undefined);
  });

  it("lists Start Here first, with eight curated entries", () => {
    const index = llmsIndex();
    const lines = index.split("\n");
    const sections = lines.filter((line) => line.startsWith("## "));
    assert.strictEqual(sections[0], "## Start Here");
    const start = lines.indexOf("## Start Here");
    const end = lines.findIndex((line, at) => at > start && line.startsWith("## "));
    const entries = lines.slice(start, end).filter((line) => line.startsWith("- ["));
    assert.strictEqual(entries.length, 8);
  });

  it("groups the remaining pages under one H2 per navigation separator, ending with Optional", () => {
    const index = llmsIndex();
    const sections = index.split("\n").filter((line) => line.startsWith("## "));
    const separators = navTree.filter((node) => node.type === "separator");
    assert.ok(separators.length > 0);
    for (const separator of separators) {
      assert.ok(sections.includes(`## ${separator.label}`));
    }
    assert.strictEqual(sections[sections.length - 1], "## Optional");
  });

  it("links every entry at an absolute Markdown URL with a note", () => {
    const index = llmsIndex();
    const firstSection = index.split("\n").findIndex((line) => line.startsWith("## "));
    const entries = index
      .split("\n")
      .slice(firstSection)
      .filter((line) => /^\s*-\s/.test(line));
    assert.ok(entries.length >= pages.length);
    for (const entry of entries) {
      const [link] = markdownLinks(entry);
      assert.ok(link, `entry without a link: ${entry}`);
      assert.ok(link.url.startsWith(`${siteUrl}/`), `relative link: ${link.url}`);
      assert.match(link.url, /\.md(?:#|$)/);
      assert.match(entry, /\):\s\S/);
    }
  });

  it("covers every page exactly once outside Start Here", () => {
    const index = llmsIndex();
    const lines = index.split("\n");
    const startHereEnd = lines.findIndex((line, at) => at > 0 && line.startsWith("## ") && line !== "## Start Here");
    const urls = markdownLinks(lines.slice(startHereEnd).join("\n")).map((link) => link.url);
    const pageUrls = pages
      .filter((page) => page.slug.length > 0)
      .map((page) => `${siteUrl}${page.mdUrl}`)
      .sort();
    assert.deepStrictEqual([...new Set(urls)].sort(), pageUrls);
  });
});

describe("llmsFull", () => {
  it("reproduces every page the contents list names", () => {
    const full = llmsFull();
    const reproduced = [...full.matchAll(/^<!-- markdown: (\S+) -->$/gm)].map((match) => match[1]);
    const contentsStart = full.indexOf("## Contents");
    const contentsEnd = full.indexOf("\n<!-- page: ", contentsStart);
    const listed = markdownLinks(full.slice(contentsStart, contentsEnd)).map((link) => link.url);
    assert.strictEqual(reproduced.length, pages.length);
    assert.deepStrictEqual(reproduced, listed);
  });

  it("introduces each page with its title, URL and Markdown URL", () => {
    const full = llmsFull();
    const page = getPage(["getting-started", "cheat-sheet"]);
    assert.ok(page);
    assert.ok(full.includes(`<!-- page: ${siteUrl}${page.url} -->`));
    assert.ok(full.includes(`<!-- markdown: ${siteUrl}${page.mdUrl} -->`));
    assert.ok(full.includes(`# ${page.title}`));
  });

  it("leaves no relative link behind", () => {
    const full = llmsFull();
    const relative = [...full.matchAll(/\]\((\.{1,2}\/[^)]*)\)/g)].map((match) => match[1]);
    assert.deepStrictEqual(relative, []);
  });
});

describe("pageMarkdown", () => {
  it("returns the page source with absolute links under a canonical header", () => {
    const page = getPage(["getting-started", "cheat-sheet"]);
    assert.ok(page);
    const markdown = pageMarkdown(["getting-started", "cheat-sheet"]);
    assert.ok(markdown?.startsWith(`<!-- canonical: ${siteUrl}${page.url} -->\n\n`));
    assert.ok(markdown.includes(`# ${page.title}`));
    const relative = [...markdown.matchAll(/\]\((\.{1,2}\/[^)]*)\)/g)].map((match) => match[1]);
    assert.deepStrictEqual(relative, []);
  });

  it("serves the documentation index from the empty slug", () => {
    const page = getPage([]);
    assert.ok(page);
    assert.ok(pageMarkdown([])?.startsWith(`<!-- canonical: ${siteUrl}${page.url} -->`));
  });

  it("returns undefined for a slug that names no page", () => {
    assert.strictEqual(pageMarkdown(["no-such-page"]), undefined);
  });
});
