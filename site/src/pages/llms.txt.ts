import type { APIRoute } from "astro";
import { llmsIndex } from "../lib/llms.ts";

export const GET: APIRoute = () => {
  return new Response(llmsIndex(), {
    headers: { "Content-Type": "text/plain; charset=utf-8" },
  });
};
