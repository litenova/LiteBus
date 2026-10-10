import { llmsFull } from '@/lib/llms';
import { defaultVersion } from '@/lib/versions';

/**
 * The complete text of the default documentation line, as one document.
 *
 * This is the companion to `/llms.txt`: the index names the pages, and this reproduces all of them. It is large by
 * design, so the index stays the entry point and this stays the deep read.
 */

export const revalidate = false;

export async function GET() {
  return new Response(await llmsFull(defaultVersion), {
    headers: {
      'Content-Type': 'text/plain; charset=utf-8',
    },
  });
}
