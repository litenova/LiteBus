import { llmsFull } from '@/lib/llms';

/**
 * The complete text of the documentation, as one document.
 *
 * This is the companion to `/llms.txt`: the index names the pages, and this reproduces all of them. It is large by
 * design, so the index stays the entry point and this stays the deep read.
 */

export const revalidate = false;

export async function GET() {
  return new Response(await llmsFull(), {
    headers: {
      'Content-Type': 'text/plain; charset=utf-8',
    },
  });
}
