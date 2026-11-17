// The sitemap: the home page, the privacy page, and every documentation page
// with its last change date. Markdown endpoints stay out: search results point
// at the rendered pages.
import type { APIRoute } from "astro";
import { pages } from "../lib/docs.ts";
import { absoluteUrl } from "../lib/seo.ts";

export const GET: APIRoute = () => {
  const buildDate = new Date().toISOString().slice(0, 10);
  const entries: { path: string; modified: string }[] = [
    { path: "/", modified: buildDate },
    { path: "/privacy", modified: buildDate },
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
