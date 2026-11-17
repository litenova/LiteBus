import type { APIRoute, GetStaticPaths } from "astro";
import { pages } from "../../lib/docs.ts";
import { pageMarkdown } from "../../lib/llms.ts";

export const getStaticPaths = (() =>
  pages
    .filter((page) => page.slug.length > 0)
    .map((page) => ({ params: { slug: page.slug.join("/") } }))) satisfies GetStaticPaths;

export const GET: APIRoute = ({ params }) => {
  const slug = typeof params.slug === "string" && params.slug.length > 0 ? params.slug.split("/") : [];
  const markdown = pageMarkdown(slug);

  if (markdown === undefined) {
    return new Response("Not found.\n", {
      status: 404,
      headers: { "Content-Type": "text/plain; charset=utf-8" },
    });
  }

  return new Response(markdown, {
    headers: { "Content-Type": "text/markdown; charset=utf-8" },
  });
};
