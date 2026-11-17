// Tests for the search and sharing metadata: title building, description
// trimming, canonical URLs, and the schema.org objects every page writes as JSON-LD.
import { describe, it } from "node:test";
import assert from "node:assert";
import {
  absoluteUrl,
  breadcrumbList,
  metaDescription,
  organization,
  pageTitle,
  socialImage,
  softwareSourceCode,
  techArticle,
  website,
} from "./seo.ts";
import { getPage } from "./docs.ts";
import { latestRelease, repositoryUrl, siteDescription, siteUrl } from "./site.ts";

describe("pageTitle", () => {
  it("joins the page title, its section and LiteBus under 60 characters", () => {
    const title = pageTitle("Cheat Sheet", "Getting Started");
    assert.strictEqual(title, "Cheat Sheet - Getting Started - LiteBus");
    assert.ok(title.length <= 60);
  });

  it("drops the section when the full form would pass 60 characters", () => {
    const title = pageTitle("A Very Long Documentation Page Title That Keeps Going", "Backend");
    assert.strictEqual(title, "A Very Long Documentation Page Title That Keeps Going - LiteBus");
  });

  it("omits the section segment when no section is given", () => {
    assert.strictEqual(pageTitle("Cheat Sheet"), "Cheat Sheet - LiteBus");
  });

  it("collapses inner whitespace before measuring", () => {
    assert.strictEqual(pageTitle("  Cheat\n  Sheet ", "Getting Started"), "Cheat Sheet - Getting Started - LiteBus");
  });
});

describe("metaDescription", () => {
  it("keeps a short description as it is", () => {
    assert.strictEqual(metaDescription("LiteBus is a mediator."), "LiteBus is a mediator.");
  });

  it("trims a long description to at most 160 characters", () => {
    const text = "LiteBus is a mediator and durable messaging library for .NET. ".repeat(10);
    assert.ok(metaDescription(text).length <= 160);
  });

  it("ends at a sentence boundary when one fits", () => {
    const text = `${"LiteBus is a mediator. ".repeat(5)}${"A trailing sentence that runs past the limit. ".repeat(5)}`;
    const description = metaDescription(text);
    assert.ok(description.endsWith("."));
    assert.ok(!description.endsWith("..."));
  });

  it("honours an explicit limit", () => {
    const text = "LiteBus is a mediator and durable messaging library for .NET. ".repeat(10);
    assert.ok(metaDescription(text, 80).length <= 80);
  });
});

describe("absoluteUrl", () => {
  it("builds an absolute URL from a site path", () => {
    assert.strictEqual(absoluteUrl("/docs/getting-started"), "https://litebus.io/docs/getting-started");
  });

  it("keeps the trailing slash of the home page", () => {
    assert.strictEqual(absoluteUrl("/"), "https://litebus.io/");
  });
});

describe("techArticle", () => {
  it("names the page, its Markdown encoding, and the website it is part of", () => {
    const page = getPage([]);
    assert.ok(page);
    const article = techArticle(page);
    assert.strictEqual(article["@type"], "TechArticle");
    assert.strictEqual(article.headline, page.title);
    assert.strictEqual(article.url, `${siteUrl}${page.url}`);
    assert.strictEqual(article.dateModified, page.lastModified);
    assert.deepStrictEqual(article.isPartOf, { "@id": `${siteUrl}/#website` });
    assert.deepStrictEqual(article.about, { "@id": `${siteUrl}/#software` });
    assert.deepStrictEqual(article.encoding, {
      "@type": "MediaObject",
      contentUrl: `${siteUrl}${page.mdUrl}`,
      encodingFormat: "text/markdown",
    });
  });
});

describe("breadcrumbList", () => {
  it("gives every entry an item, with the current page last under its own URL", () => {
    const page = getPage(["getting-started", "cheat-sheet"]);
    assert.ok(page);
    const list = breadcrumbList(page) as { itemListElement: Record<string, unknown>[] };
    assert.strictEqual(list["@type"], "BreadcrumbList");
    const entries = list.itemListElement;
    assert.ok(entries.length >= 2);
    assert.deepStrictEqual(
      entries.map((entry) => entry.position),
      entries.map((_, index) => index + 1),
    );
    assert.ok(entries.every((entry) => typeof entry.item === "string" && (entry.item as string).startsWith(siteUrl)));
    assert.strictEqual(entries[0].name, "Docs");
    assert.strictEqual(entries[entries.length - 1].item, `${siteUrl}${page.url}`);
  });
});

describe("the site objects", () => {
  it("names the WebSite, Organization and SoftwareSourceCode types", () => {
    assert.strictEqual(website["@type"], "WebSite");
    assert.strictEqual(organization["@type"], "Organization");
    assert.strictEqual(softwareSourceCode["@type"], "SoftwareSourceCode");
  });

  it("describes the LiteBus library with its repository, language and release", () => {
    assert.strictEqual(softwareSourceCode.codeRepository, repositoryUrl);
    assert.strictEqual(softwareSourceCode.programmingLanguage, "C#");
    assert.strictEqual(softwareSourceCode.runtimePlatform, ".NET");
    assert.strictEqual(softwareSourceCode.version, latestRelease);
    assert.strictEqual(softwareSourceCode.license, "https://opensource.org/licenses/MIT");
  });

  it("points the social image at the site with its true dimensions", () => {
    assert.strictEqual(socialImage.url, `${siteUrl}/og.png`);
    assert.strictEqual(socialImage.width, 1280);
    assert.strictEqual(socialImage.height, 640);
    assert.ok((socialImage.alt as string).length > 0);
    assert.ok((socialImage.alt as string).includes(siteDescription.slice(0, 20)));
  });
});
