import { pageMarkdown } from '@/lib/llms';
import { docsBasePath } from '@/lib/site';
import { getSource } from '@/lib/source';

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
  const source = await getSource();

  return source.getPages().map((page) => ({
    path: page.url.split('/').filter((segment) => segment.length > 0),
  }));
}

export async function GET(_request: Request, { params }: { params: Promise<{ path: string[] }> }) {
  const pathname = `/${(await params).path.join('/')}`;

  // Only a documentation path resolves here. Without this the slug of an unrelated path would be looked up anyway, and
  // `/getting-started.md` would answer with the page that belongs at `/docs/getting-started.md`.
  if (pathname !== docsBasePath && !pathname.startsWith(`${docsBasePath}/`)) {
    return notFound();
  }

  const slug = pathname === docsBasePath ? [] : pathname.slice(docsBasePath.length + 1).split('/');
  const markdown = await pageMarkdown(slug);

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
