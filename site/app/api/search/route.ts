import { createFromSource } from 'fumadocs-core/search/server';
import { getSource } from '@/lib/source';

// One index over the one documentation set.
const search = createFromSource(() => getSource());

export async function GET(request: Request) {
  const url = new URL(request.url);

  // Older clients send a `tag` that used to select a per-version index, and the pages in this index carry no tags of
  // their own. Forwarding it would have Orama filter every result away.
  url.searchParams.delete('tag');

  return search.GET(new Request(url, request));
}
