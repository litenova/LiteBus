// The sitemap: the home page, the privacy page, and every documentation page
// with its last change date. Markdown endpoints stay out: search results point
// at the rendered pages.
import type { APIRoute } from "astro";
import { execFileSync } from "node:child_process";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { pages } from "../lib/docs.ts";
import { absoluteUrl } from "../lib/seo.ts";

/** The newest last-change date across all documentation pages, or the build date when there are none. */
function newestDocsDate(buildDate: string): string {
  let newest = "";
  for (const page of pages) {
    if (page.lastModified > newest) {
      newest = page.lastModified;
    }
  }
  return newest === "" ? buildDate : newest;
}

/** The last-commit date of the privacy page source, or the build date when git cannot date it. */
function privacyDate(buildDate: string): string {
  try {
    const source = join(dirname(fileURLToPath(import.meta.url)), "privacy.astro");
    const date = execFileSync("git", ["log", "-1", "--format=%cs", "--", source], { encoding: "utf8" }).trim();
    return /^\d{4}-\d{2}-\d{2}$/.test(date) ? date : buildDate;
  } catch {
    return buildDate;
  }
}

export const GET: APIRoute = () => {
  const buildDate = new Date().toISOString().slice(0, 10);
  const entries: { path: string; modified: string }[] = [
    { path: "/", modified: newestDocsDate(buildDate) },
    { path: "/privacy", modified: privacyDate(buildDate) },
    ...pages.map((page) => ({ path: page.url, modified: page.lastModified })),
  ];
  const urls = entries
    .map((entry) =>
      ["  <url>", `    <loc>${absoluteUrl(entry.path)}</loc>`, `    <lastmod>${entry.modified}</lastmod>`, "  </url>"].join(
        "\n",
      ),
    )
    .join("\n");
  const body = `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${urls}\n</urlset>\n`;
  return new Response(body, { headers: { "Content-Type": "application/xml; charset=utf-8" } });
};
