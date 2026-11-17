// Tests for the Markdown renderer: heading ids and the table of contents,
// table wrapping, code-block figures, link rewriting, diagrams, and snippets.
import { describe, it } from "node:test";
import assert from "node:assert";
import { getPage, type DocPage } from "./docs.ts";
import { highlight, renderPage } from "./render-markdown.ts";

/** A page carrying test Markdown, addressed as the cheat sheet for link tests. */
function testPage(markdown: string): DocPage {
  const base = getPage(["getting-started", "cheat-sheet"]);
  assert.ok(base !== undefined);
  return { ...base, markdown };
}

describe("headings and table of contents", () => {
  it("removes the leading H1 and ids the rest GitHub-style", async () => {
    const { html, toc } = await renderPage(testPage("# Title\n\n## Hello, World!\n\n### Sub Section\n"));
    assert.ok(!html.includes("<h1"), html);
    assert.ok(html.includes('<h2 id="hello-world">'), html);
    assert.ok(html.includes('<h3 id="sub-section">'), html);
    assert.deepStrictEqual(toc, [
      { depth: 2, id: "hello-world", text: "Hello, World!" },
      { depth: 3, id: "sub-section", text: "Sub Section" },
    ]);
  });

  it("numbers repeated headings uniquely", async () => {
    const { html, toc } = await renderPage(testPage("# Title\n\n## Setup\n\n## Setup\n"));
    assert.ok(html.includes('<h2 id="setup">'), html);
    assert.ok(html.includes('<h2 id="setup-2">'), html);
    assert.deepStrictEqual(
      toc.map((entry) => entry.id),
      ["setup", "setup-2"],
    );
  });

  it("keeps H4 headings out of the table of contents", async () => {
    const { html, toc } = await renderPage(testPage("# Title\n\n## Setup\n\n#### Detail\n"));
    assert.ok(html.includes('<h4 id="detail">'), html);
    assert.deepStrictEqual(
      toc.map((entry) => entry.id),
      ["setup"],
    );
  });

  it("links each heading to itself", async () => {
    const { html } = await renderPage(testPage("# Title\n\n## Setup\n"));
    assert.ok(html.includes('<a class="anchor" href="#setup"'), html);
  });
});

describe("tables", () => {
  it("wraps tables in a scrolling region with scoped headers", async () => {
    const { html } = await renderPage(testPage("# Title\n\n| A | B |\n|---|---|\n| 1 | 2 |\n"));
    assert.ok(html.includes('<div class="table-wrap"'), html);
    assert.ok(html.includes('<th scope="col">A</th>'), html);
  });
});

describe("code blocks", () => {
  it("wraps fences in a figure with a language label and a copy button", async () => {
    const { html } = await renderPage(testPage("# Title\n\n```csharp\nvar x = 1;\n```\n"));
    assert.ok(html.includes('<figure class="code-block"'), html);
    assert.ok(html.includes('<span class="code-lang">C#</span>'), html);
    assert.ok(html.includes('<button type="button" class="copy"'), html);
    assert.ok(html.includes('data-copy="var x = 1;"'), html);
  });
});

describe("links", () => {
  it("rewrites relative Markdown links to page URLs and keeps fragments", async () => {
    const { html } = await renderPage(testPage("# Title\n\n[Sample](sample.md) and [Part](sample.md#setup).\n"));
    assert.ok(html.includes('<a href="/docs/getting-started/sample">'), html);
    assert.ok(html.includes('<a href="/docs/getting-started/sample#setup">'), html);
  });

  it("marks external links as such", async () => {
    const { html } = await renderPage(testPage("# Title\n\n[Elsewhere](https://example.com/page).\n"));
    assert.ok(html.includes('<a href="https://example.com/page" rel="noopener">'), html);
  });
});

describe("diagrams", () => {
  it("renders Mermaid fences to inline diagrams", async () => {
    const { html } = await renderPage(testPage("# Title\n\n```mermaid\nflowchart LR\n    A --> B\n```\n"));
    assert.ok(html.includes("ondine-diagram"), html);
    assert.ok(html.includes("<svg"), html);
  });
});

describe("highlight", () => {
  it("highlights a snippet with the page code-block classes", async () => {
    const html = await highlight("var x = 1;", "csharp");
    assert.ok(html.includes('<figure class="code-block"'), html);
    assert.ok(html.includes('<span class="code-lang">C#</span>'), html);
    assert.ok(html.includes('<button type="button" class="copy"'), html);
    assert.ok(html.includes('data-copy="var x = 1;"'), html);
  });
});
