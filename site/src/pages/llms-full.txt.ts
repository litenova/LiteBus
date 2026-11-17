import type { APIRoute } from "astro";
import { llmsFull } from "../lib/llms.ts";

export const GET: APIRoute = () => {
  return new Response(llmsFull(), {
    headers: { "Content-Type": "text/plain; charset=utf-8" },
  });
};
