import { llmsIndex } from '@/lib/llms';
import { defaultVersion } from '@/lib/versions';

/**
 * The llms.txt index for the default documentation line.
 *
 * The convention has clients probe the site root first, so the default version answers here rather than under its
 * `/docs` prefix. `next.config.mjs` redirects `/docs/llms.txt` to this route for clients that guess the prefixed form.
 */

// Nothing here changes between deployments, so the file is built once rather than on request.
export const revalidate = false;

export async function GET() {
  return new Response(await llmsIndex(defaultVersion), {
    headers: {
      'Content-Type': 'text/plain; charset=utf-8',
    },
  });
}
