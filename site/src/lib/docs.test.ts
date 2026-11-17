// Tests for the documentation loader: slug and URL mapping, title and
// description extraction, navigation order with separators, neighbors,
// breadcrumbs, reading time, and link resolution.
import { describe, it } from "node:test";
import assert from "node:assert";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  absoluteMarkdownHref,
  breadcrumbs,
  getPage,
  navTree,
  pageNeighbors,
  pages,
  readingTime,
  resolveDocHref,
  type DocPage,
  type NavNode,
} from "./docs.ts";

/** The content folder beside the site sources. */
const contentDir = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "..", "content", "docs");

/** Every Markdown file under a folder. */
function diskFiles(dir: string): string[] {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      return diskFiles(full);
    }
    return entry.name.toLowerCase().endsWith(".md") ? [full] : [];
  });
}

/** A page that must exist, by slug. */
function mustGetPage(slug: string[]): DocPage {
  const page = getPage(slug);
  assert.ok(page !== undefined, `missing page for slug [${slug.join(", ")}]`);
  return page;
}

/** A folder node that must exist among nodes, by label. */
function mustGetFolder(nodes: NavNode[], label: string): { label: string; index?: DocPage; children: NavNode[] } {
  const found = nodes.find((node) => node.type === "folder" && node.label === label);
  assert.ok(found !== undefined, `missing folder "${label}"`);
  assert.ok(found.type === "folder", `missing folder "${label}"`);
  return found;
}

describe("slugs and URLs", () => {
  it("maps index.md to the docs root", () => {
    const page = mustGetPage([]);
    assert.strictEqual(page.file, "index.md");
    assert.strictEqual(page.url, "/docs");
    assert.strictEqual(page.mdUrl, "/docs.md");
  });

  it("maps a README.md to its folder URL", () => {
    const page = mustGetPage(["getting-started"]);
    assert.strictEqual(page.file, "getting-started/README.md");
    assert.strictEqual(page.url, "/docs/getting-started");
    assert.strictEqual(page.mdUrl, "/docs/getting-started.md");
  });

  it("maps a nested page to its path URL", () => {
    const page = mustGetPage(["getting-started", "cheat-sheet"]);
    assert.strictEqual(page.file, "getting-started/cheat-sheet.md");
    assert.strictEqual(page.url, "/docs/getting-started/cheat-sheet");
    assert.strictEqual(page.mdUrl, "/docs/getting-started/cheat-sheet.md");
  });

  it("maps a deep catalog page through both folders", () => {
    const page = mustGetPage(["catalog", "storage", "storage.role.lease"]);
    assert.strictEqual(page.url, "/docs/catalog/storage/storage.role.lease");
  });

  it("returns undefined for an unknown slug", () => {
    assert.strictEqual(getPage(["no-such-page"]), undefined);
  });
});

describe("pages", () => {
  it("holds one entry per Markdown file on disk", () => {
    const onDisk = diskFiles(contentDir).sort();
    assert.ok(onDisk.length > 0);
    assert.strictEqual(pages.length, onDisk.length);
    const files = pages.map((page) => page.file).sort();
    const expected = onDisk.map((full) => path.relative(contentDir, full).split(path.sep).join("/")).sort();
    assert.deepStrictEqual(files, expected);
  });

  it("gives every page a title, description, URL, section and date", () => {
    for (const page of pages) {
      assert.ok(page.title.length > 0, page.file);
      assert.ok(page.description.length > 0, page.file);
      assert.ok(page.url.startsWith("/docs"), page.file);
      assert.strictEqual(page.mdUrl, `${page.url}.md`, page.file);
      assert.ok(page.section.length > 0, page.file);
      assert.match(page.lastModified, /^\d{4}-\d{2}-\d{2}$/, page.file);
      assert.ok(!page.markdown.includes("\r"), page.file);
    }
  });

  it("reads the title from the first H1", () => {
    assert.strictEqual(mustGetPage([]).title, "Documentation");
    assert.strictEqual(mustGetPage(["getting-started"]).title, "Getting Started");
    assert.strictEqual(mustGetPage(["getting-started", "cheat-sheet"]).title, "LiteBus Cheat Sheet");
  });

  it("reads the description from the intro paragraph", () => {
    assert.strictEqual(
      mustGetPage([]).description,
      "LiteBus provides command, query, and event mediation for .NET applications.",
    );
  });

  it("labels sections from the top-level folders", () => {
    assert.strictEqual(mustGetPage([]).section, "Documentation");
    assert.strictEqual(mustGetPage(["getting-started", "cheat-sheet"]).section, "Getting Started");
    assert.strictEqual(mustGetPage(["catalog", "storage"]).section, "Catalog");
  });
});

describe("navigation", () => {
  it("opens with the docs index page", () => {
    const first = navTree[0];
    assert.ok(first !== undefined);
    assert.ok(first.type === "page");
    assert.strictEqual(first.page.file, "index.md");
  });

  it("keeps the root separators in meta.json order", () => {
    const labels = navTree.filter((node) => node.type === "separator").map((node) => node.label);
    assert.deepStrictEqual(labels, ["Learn", "Build", "Contributor Reference"]);
  });

  it("leads a folder with its index and orders the rest by meta.json", () => {
    const folder = mustGetFolder(navTree, "Getting Started");
    assert.strictEqual(folder.index?.file, "getting-started/README.md");
    assert.deepStrictEqual(
      folder.children.map((node) => (node.type === "page" ? node.page.file : node.type)),
      [
        "getting-started/sample.md",
        "getting-started/cheat-sheet.md",
        "getting-started/cookbook.md",
        "getting-started/best-practices.md",
      ],
    );
  });

  it("nests the catalog areas below the catalog", () => {
    const catalog = mustGetFolder(navTree, "Catalog");
    assert.strictEqual(catalog.index, undefined);
    assert.deepStrictEqual(
      catalog.children.map((node) => (node.type === "folder" ? node.label : node.type)),
      [
        "Analyzers",
        "Dispatch",
        "Durable Core",
        "Hosting",
        "Ingress",
        "Mediator",
        "Runtime",
        "Saga",
        "Storage",
        "Transport",
      ],
    );
  });

  it("keeps separators inside folders", () => {
    const roadmap = mustGetFolder(navTree, "Roadmap");
    const labels = roadmap.children.filter((node) => node.type === "separator").map((node) => node.label);
    assert.deepStrictEqual(labels, ["Design Notes"]);
  });

  it("leaves folders without an index page unlabeled by one", () => {
    assert.strictEqual(mustGetFolder(navTree, "Reference").index, undefined);
  });
});

describe("neighbors", () => {
  it("links every page to its reading-order neighbors", () => {
    const first = pages[0];
    assert.ok(first !== undefined);
    assert.strictEqual(pageNeighbors(first).prev, undefined);
    assert.ok(pageNeighbors(first).next !== undefined);
    const last = pages[pages.length - 1];
    assert.ok(last !== undefined);
    assert.strictEqual(pageNeighbors(last).next, undefined);
    for (const page of pages) {
      const { prev, next } = pageNeighbors(page);
      if (prev !== undefined) {
        assert.strictEqual(pageNeighbors(prev).next?.file, page.file);
      }
      if (next !== undefined) {
        assert.strictEqual(pageNeighbors(next).prev?.file, page.file);
      }
    }
  });
});

describe("breadcrumbs", () => {
  it("renders the docs root as one crumb without a URL", () => {
    assert.deepStrictEqual(breadcrumbs(mustGetPage([])), [{ label: "Docs" }]);
  });

  it("walks folders to the current page, which carries no URL", () => {
    assert.deepStrictEqual(breadcrumbs(mustGetPage(["getting-started", "cheat-sheet"])), [
      { label: "Docs", url: "/docs" },
      { label: "Getting Started", url: "/docs/getting-started" },
      { label: "LiteBus Cheat Sheet" },
    ]);
  });

  it("ends a folder index on its folder without a URL", () => {
    assert.deepStrictEqual(breadcrumbs(mustGetPage(["getting-started"])), [
      { label: "Docs", url: "/docs" },
      { label: "Getting Started" },
    ]);
  });

  it("names folders without an index page but links nothing to them", () => {
    assert.deepStrictEqual(breadcrumbs(mustGetPage(["catalog", "storage", "storage.role.lease"])), [
      { label: "Docs", url: "/docs" },
      { label: "Catalog" },
      { label: "Storage", url: "/docs/catalog/storage" },
      { label: mustGetPage(["catalog", "storage", "storage.role.lease"]).title },
    ]);
  });
});

describe("readingTime", () => {
  it("reports at least one minute for every page", () => {
    for (const page of pages) {
      const minutes = readingTime(page);
      assert.ok(Number.isInteger(minutes) && minutes >= 1, page.file);
    }
  });
});

describe("resolveDocHref", () => {
  it("resolves a bare sibling name to its page URL", () => {
    const page = mustGetPage(["getting-started", "cheat-sheet"]);
    assert.strictEqual(resolveDocHref("sample.md", page), "/docs/getting-started/sample");
    assert.strictEqual(resolveDocHref("./sample.md", page), "/docs/getting-started/sample");
  });

  it("maps a folder README to the folder URL", () => {
    const page = mustGetPage(["getting-started", "cheat-sheet"]);
    assert.strictEqual(resolveDocHref("README.md", page), "/docs/getting-started");
    assert.strictEqual(resolveDocHref("../migration/README.md", page), "/docs/migration");
  });

  it("climbs to the docs root index", () => {
    const page = mustGetPage(["catalog", "storage"]);
    assert.strictEqual(resolveDocHref("../../index.md", page), "/docs");
  });

  it("keeps the fragment of a page link", () => {
    const page = mustGetPage(["getting-started", "cheat-sheet"]);
    assert.strictEqual(resolveDocHref("sample.md#setup", page), "/docs/getting-started/sample#setup");
  });

  it("leaves absolute, mail and fragment links alone", () => {
    const page = mustGetPage(["getting-started", "cheat-sheet"]);
    assert.strictEqual(resolveDocHref("https://example.com/a.md", page), "https://example.com/a.md");
    assert.strictEqual(resolveDocHref("mailto:someone@example.com", page), "mailto:someone@example.com");
    assert.strictEqual(resolveDocHref("#setup", page), "#setup");
    assert.strictEqual(resolveDocHref("diagram.svg", page), "diagram.svg");
  });

  it("sends links outside the docs to the repository sources", () => {
    const page = mustGetPage(["catalog", "storage"]);
    const resolved = resolveDocHref("../../../samples/app.md", page);
    assert.ok(resolved.startsWith("https://github.com/litenova/LiteBus/blob/v7/"), resolved);
    assert.ok(resolved.includes("samples/app.md"), resolved);
  });
});

describe("absoluteMarkdownHref", () => {
  it("points page links at absolute Markdown endpoints", () => {
    const page = mustGetPage(["getting-started", "cheat-sheet"]);
    assert.strictEqual(
      absoluteMarkdownHref("sample.md", page),
      "https://litebus.io/docs/getting-started/sample.md",
    );
    assert.strictEqual(
      absoluteMarkdownHref("sample.md#setup", page),
      "https://litebus.io/docs/getting-started/sample.md#setup",
    );
  });

  it("passes absolute targets through untouched", () => {
    const page = mustGetPage(["catalog", "storage"]);
    const fallback = resolveDocHref("../../../samples/app.md", page);
    assert.strictEqual(absoluteMarkdownHref("../../../samples/app.md", page), fallback);
    assert.strictEqual(
      absoluteMarkdownHref("https://example.com/a.md", page),
      "https://example.com/a.md",
    );
  });
});
