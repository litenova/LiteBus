import type { APIRoute } from "astro";
import { pageMarkdown } from "../lib/llms.ts";

export const GET: APIRoute = () => {
  const markdown = pageMarkdown([]);

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
