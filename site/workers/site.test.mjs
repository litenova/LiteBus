import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { markdownPathFor, prefersMarkdown } from "./site.js";

describe("prefersMarkdown", () => {
    it("returns false for the Chrome navigation Accept header", () => {
        assert.equal(
            prefersMarkdown(
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7",
            ),
            false,
        );
    });

    it("returns false for the Firefox navigation Accept header", () => {
        assert.equal(
            prefersMarkdown(
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/png,image/svg+xml,*/*;q=0.8",
            ),
            false,
        );
    });

    it("returns false for the Safari navigation Accept header", () => {
        assert.equal(prefersMarkdown("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"), false);
    });

    it("returns true when only Markdown is accepted", () => {
        assert.equal(prefersMarkdown("text/markdown"), true);
    });

    it("returns true for Markdown with lower-priority plain text", () => {
        assert.equal(prefersMarkdown("text/markdown, text/plain;q=0.5"), true);
    });

    it("returns true when Markdown outranks HTML", () => {
        assert.equal(prefersMarkdown("text/markdown;q=1, text/html;q=0.5"), true);
    });

    it("returns false when HTML outranks Markdown", () => {
        assert.equal(prefersMarkdown("text/markdown;q=0.5, text/html;q=1"), false);
    });

    it("returns false when Markdown and HTML rank equally", () => {
        assert.equal(prefersMarkdown("text/markdown;q=0.8, text/html;q=0.8"), false);
    });

    it("returns false when Markdown is absent", () => {
        assert.equal(prefersMarkdown("text/html, */*;q=0.8"), false);
    });

    it("returns false when the header is missing or empty", () => {
        assert.equal(prefersMarkdown(null), false);
        assert.equal(prefersMarkdown(undefined), false);
        assert.equal(prefersMarkdown(""), false);
    });

    it("returns false when Markdown is explicitly refused with q=0", () => {
        assert.equal(prefersMarkdown("text/markdown;q=0, text/html;q=0.8"), false);
    });

    it("returns true when HTML is refused and Markdown is accepted", () => {
        assert.equal(prefersMarkdown("text/markdown, text/html;q=0"), true);
    });

    it("returns false when a wildcard accompanies Markdown without HTML", () => {
        assert.equal(prefersMarkdown("text/markdown, */*;q=0.8"), false);
    });

    it("matches media types case-insensitively and ignores spacing", () => {
        assert.equal(prefersMarkdown("Text/Markdown ; q=1 , Text/Html ; q=0.5"), true);
    });
});

describe("markdownPathFor", () => {
    it("maps the docs index to its Markdown asset", () => {
        assert.equal(markdownPathFor("/docs"), "/docs.md");
    });

    it("drops a trailing slash before mapping", () => {
        assert.equal(markdownPathFor("/docs/"), "/docs.md");
        assert.equal(markdownPathFor("/docs/getting-started/"), "/docs/getting-started.md");
    });

    it("maps nested pages to their Markdown assets", () => {
        assert.equal(markdownPathFor("/docs/a/b"), "/docs/a/b.md");
    });

    it("returns null for Markdown URLs", () => {
        assert.equal(markdownPathFor("/docs/a.md"), null);
        assert.equal(markdownPathFor("/docs.md"), null);
    });

    it("returns null outside the documentation tree", () => {
        assert.equal(markdownPathFor("/llms.txt"), null);
        assert.equal(markdownPathFor("/"), null);
        assert.equal(markdownPathFor("/privacy"), null);
    });

    it("returns null for paths that only start with /docs", () => {
        assert.equal(markdownPathFor("/docsx"), null);
        assert.equal(markdownPathFor("/documentation"), null);
    });

    it("returns null for non-string input", () => {
        assert.equal(markdownPathFor(null), null);
        assert.equal(markdownPathFor(undefined), null);
    });
});
