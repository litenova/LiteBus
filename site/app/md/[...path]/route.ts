import { pageMarkdown } from '@/lib/llms';
import { getAllSources } from '@/lib/source';
import { slugFromPathname, versionBasePath, versionFromPathname } from '@/lib/versions';

/**
 * One documentation page as Markdown.
 *
 * Reached by appending `.md` to any documentation URL, which the convention names as the way to offer a clean source
 * copy of a rendered page. `next.config.mjs` rewrites `/docs/concepts/commands.md` to `/md/docs/concepts/commands`,
 * because a page and a route handler cannot both answer at one path in the App Router. The rewrite runs after the
 * filesystem check, so it never shadows a real file, and the `.md` links in `llms.txt` and `llms-full.txt` all land
 * here.
 */

export const revalidate = false;

export async function generateStaticParams() {
  const sources = await getAllSources();

  return sources.flatMap(({ source }) =>
    source.getPages().map((page) => ({
      path: page.url.split('/').filter((segment) => segment.length > 0),
    })),
  );
}

export async function GET(_request: Request, { params }: { params: Promise<{ path: string[] }> }) {
  const pathname = `/${(await params).path.join('/')}`;
  const version = versionFromPathname(pathname);
  const base = versionBasePath(version);

  // Only a documentation path resolves here. Without this the slug of an unrelated path would be looked up anyway, and
  // `/getting-started.md` would answer with the page that belongs at `/docs/getting-started.md`.
  if (pathname !== base && !pathname.startsWith(`${base}/`)) {
    return notFound();
  }

  const markdown = await pageMarkdown(version, slugFromPathname(pathname));

  if (markdown === undefined) {
    return notFound();
  }

  return new Response(markdown, {
    headers: {
      'Content-Type': 'text/markdown; charset=utf-8',
    },
  });
}

/** A plain-text 404, so a client that guessed a path gets an answer in the format it asked for. */
function notFound(): Response {
  return new Response('Not found.\n', {
    status: 404,
    headers: {
      'Content-Type': 'text/plain; charset=utf-8',
    },
  });
}
