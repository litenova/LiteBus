import { llmsIndex } from '@/lib/llms';

/**
 * The llms.txt index for the documentation.
 *
 * The convention has clients probe the site root first, so the index answers here rather than under the `/docs`
 * prefix. `next.config.mjs` redirects `/docs/llms.txt` to this route for clients that guess the prefixed form.
 */

// Nothing here changes between deployments, so the file is built once rather than on request.
export const revalidate = false;

export async function GET() {
  return new Response(await llmsIndex(), {
    headers: {
      'Content-Type': 'text/plain; charset=utf-8',
    },
  });
}
