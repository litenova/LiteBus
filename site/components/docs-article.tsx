import type { Metadata } from 'next';
import type { ComponentProps } from 'react';
import { Fragment } from 'react';
import Link from 'next/link';
import { notFound } from 'next/navigation';
import type { Node } from 'fumadocs-core/page-tree';
import { CodeCopyButton } from '@/components/code-copy-button';
import { resolveDocHref } from '@/lib/doc-links';
import { getSource } from '@/lib/source';
import { siteDescription, siteName } from '@/lib/site';
import { type DocsVersion, defaultVersion, versionBasePath } from '@/lib/versions';

/**
 * Metadata for one documentation page.
 *
 * A pre-release version is excluded from indexing so that a search engine does not offer documentation for a package
 * most readers should not install yet, ahead of the stable page that answers the same question.
 */
export async function docsMetadata(version: DocsVersion, slug: string[]): Promise<Metadata> {
  const page = (await getSource(version.id)).getPage(slug);

  if (!page) {
    notFound();
  }

  const versionSuffix = version.default ? '' : ` (${version.label})`;
  const title = `${page.data.title ?? siteName}${versionSuffix}`;
  const description = page.data.description ?? siteDescription;

  return {
    title,
    description,
    alternates: {
      canonical: page.url,
    },
    robots: version.status === 'prerelease' ? { index: false, follow: true } : undefined,
    openGraph: {
      type: 'article',
      url: page.url,
      title,
      description,
    },
    twitter: {
      card: 'summary_large_image',
      title,
      description,
    },
  };
}

/**
 * One page in navigation order, with the folders above it for the breadcrumb.
 */
type FlatPage = {
  readonly name: string;
  readonly url: string;
  readonly folders: readonly string[];
};

/**
 * The display text of a page tree node, falling back when it is not a plain string.
 */
function nodeText(name: unknown, fallback: string): string {
  return typeof name === 'string' && name.length > 0 ? name : fallback;
}

/**
 * Every page under a list of nodes, in the order the navigation shows them.
 *
 * A folder's index page is emitted ahead of the folder's other children. Callers dedupe on URL: whether an index
 * page also stays in `children` is a page tree detail this does not depend on.
 */
function flattenPages(nodes: readonly Node[], folders: readonly string[]): FlatPage[] {
  const pages: FlatPage[] = [];

  for (const node of nodes) {
    if (node.type === 'page') {
      pages.push({ name: nodeText(node.name, node.url), url: node.url, folders });
    } else if (node.type === 'folder') {
      const name = nodeText(node.name, '');
      const below = name.length > 0 ? [...folders, name] : [...folders];

      if (node.index !== undefined) {
        pages.push({
          name: nodeText(node.index.name, node.index.url),
          url: node.index.url,
          folders: below,
        });
      }

      pages.push(...flattenPages(node.children, below));
    }
  }

  return pages;
}

/**
 * The breadcrumb segments for a page: its folders, then its own name.
 *
 * A folder index page usually repeats its folder's name, so a repeated segment is shown once.
 */
function crumbsFor(page: FlatPage): readonly string[] {
  const segments = [...page.folders, page.name];

  return segments.filter((segment, index) => index === 0 || segment !== segments[index - 1]);
}

/**
 * One documentation page: crumbs, the Markdown body, an on-page table of contents, and the pager.
 *
 * The body renders with only two Markdown overrides: links resolve through `resolveDocHref` so the sources can keep
 * linking by relative path, and code blocks gain a copy button. Internal links use the router; anything else renders
 * as a plain anchor.
 */
export async function DocsArticle({
  version,
  slug,
}: Readonly<{ version: DocsVersion; slug: string[] }>) {
  const source = await getSource(version.id);
  const page = source.getPage(slug);

  if (!page) {
    notFound();
  }

  const renderer = await page.data.load();
  const rendered = await renderer.render({
    a: ({ href, ...props }: ComponentProps<'a'>) => {
      if (href === undefined) {
        return <a {...props} />;
      }

      const resolved = resolveDocHref(href, page, source, version);

      if (resolved.startsWith('/')) {
        return <Link href={resolved} {...props} />;
      }

      return <a href={resolved} {...props} />;
    },
    pre: (props: ComponentProps<'pre'>) => (
      <div className="code">
        <pre {...props} />
        <CodeCopyButton />
      </div>
    ),
  });

  // Tree order first, then any page the tree does not reach, so every served page has a pager position.
  const ordered: FlatPage[] = [];
  const seen = new Set<string>();

  for (const entry of flattenPages(source.getPageTree().children, [])) {
    if (!seen.has(entry.url)) {
      seen.add(entry.url);
      ordered.push(entry);
    }
  }

  for (const candidate of source.getPages()) {
    if (!seen.has(candidate.url)) {
      seen.add(candidate.url);
      ordered.push({
        name: candidate.data.title ?? candidate.url,
        url: candidate.url,
        folders: [],
      });
    }
  }

  const currentIndex = ordered.findIndex((entry) => entry.url === page.url);
  const current = currentIndex === -1 ? undefined : ordered[currentIndex];
  const previous = currentIndex > 0 ? ordered[currentIndex - 1] : undefined;
  const next =
    currentIndex === -1 || currentIndex + 1 >= ordered.length
      ? undefined
      : ordered[currentIndex + 1];
  const headings = rendered.toc.filter((entry) => entry.depth === 2);
  const crumbs = current === undefined ? [] : crumbsFor(current);

  return (
    <main id="main" className="page" tabIndex={-1}>
      <p className="crumbs">
        {slug.length === 0 ? (
          'Docs'
        ) : (
          <>
            <Link href={versionBasePath(version)}>Docs</Link>
            {crumbs.map((segment, index) => (
              <Fragment key={`${segment}-${index}`}> / {segment}</Fragment>
            ))}
          </>
        )}
      </p>
      <a className="jump" href="#docs-nav">
        All documentation pages
      </a>
      {version.status === 'prerelease' ? (
        <aside className="callout warn">
          <strong>{version.label} is a pre-release.</strong> This page documents{' '}
          <code>{version.release}</code>, which ships to NuGet as a pre-release package and may still
          change.{' '}
          <Link href={versionBasePath(defaultVersion)}>
            Read the {defaultVersion.label} documentation
          </Link>{' '}
          for the version most applications should install.
        </aside>
      ) : null}
      {headings.length >= 3 ? (
        <nav className="toc" aria-label="On this page">
          <p>On this page</p>
          <ol>
            {headings.map((entry) => (
              <li key={entry.url}>
                <a href={entry.url}>{entry.title}</a>
              </li>
            ))}
          </ol>
        </nav>
      ) : null}
      <div className="prose">{rendered.body}</div>
      {previous === undefined && next === undefined ? null : (
        <nav className="pager" aria-label="Previous and next page">
          {previous === undefined ? null : (
            <Link href={previous.url}>
              <span>Previous</span>
              {previous.name}
            </Link>
          )}
          {next === undefined ? null : (
            <Link href={next.url} className="next-page">
              <span>Next</span>
              {next.name}
            </Link>
          )}
        </nav>
      )}
    </main>
  );
}
