import { createFromSource } from 'fumadocs-core/search/server';
import { getSource } from '@/lib/source';
import { defaultVersion, docsVersions } from '@/lib/versions';

// One index per version, chosen by the `tag` the search dialog sends. A single index across every version would answer
// a question asked from the v7 documentation with v6 pages, which is the mistake versioning exists to prevent.
const searchByVersion = new Map(
  docsVersions.map(
    (version) => [version.id, createFromSource(() => getSource(version.id))] as const,
  ),
);

export async function GET(request: Request) {
  const url = new URL(request.url);
  const requested = url.searchParams.get('tag') ?? defaultVersion.id;
  const search = searchByVersion.get(requested);

  if (search === undefined) {
    return Response.json([]);
  }

  // The tag selects the index rather than filtering inside one, and the pages in these indexes carry no tags of their
  // own. Forwarding it would have Orama filter every result away.
  url.searchParams.delete('tag');

  return search.GET(new Request(url, request));
}
