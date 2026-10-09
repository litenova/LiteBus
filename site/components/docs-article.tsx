import type { Metadata } from 'next';
import type { ComponentProps, ReactNode } from 'react';
import { Children, Fragment, isValidElement } from 'react';
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
 * The plain text inside a rendered node, used to read a blockquote's leading marker.
 */
function textContent(node: ReactNode): string {
  if (typeof node === 'string' || typeof node === 'number') {
    return String(node);
  }

  if (Array.isArray(node)) {
    return node.map(textContent).join('');
  }

  if (isValidElement(node)) {
    const props = node.props as { children?: ReactNode };

    return textContent(props.children);
  }

  return '';
}

/**
 * The text of the leading strong segment in a paragraph, or null when text comes first.
 *
 * Whitespace-only strings are skipped so Markdown spacing does not disqualify a marker.
 */
function leadingStrongText(paragraphChildren: ReactNode): string | null {
  const items = Children.toArray(paragraphChildren);

  for (const item of items) {
    if (typeof item === 'string') {
      if (item.trim().length === 0) {
        continue;
      }

      return null;
    }

    if (!isValidElement(item)) {
      return null;
    }

    if (item.type === 'strong') {
      const props = item.props as { children?: ReactNode };

      return textContent(props.children);
    }

    return null;
  }

  return null;
}

/**
 * The Fuse box a blockquote maps to, decided by its first paragraph's leading marker.
 *
 * A first paragraph starting with `This page is for` renders as the audience box. A first paragraph starting with
 * `Note` or `Warning` renders as the matching callout. Anything else stays a plain blockquote so the `.md`
 * endpoints and `llms-full.txt` keep reading as a quote.
 */
function classifyBlockquote(children: ReactNode): 'audience' | 'note' | 'warning' | null {
  const top = Children.toArray(children);

  for (const child of top) {
    if (typeof child === 'string') {
      if (child.trim().length === 0) {
        continue;
      }

      return null;
    }

    if (!isValidElement(child)) {
      continue;
    }

    if (child.type !== 'p') {
      continue;
    }

    const props = child.props as { children?: ReactNode };
    const leading = leadingStrongText(props.children);

    if (leading === null) {
      return null;
    }

    const marker = leading.trim().replace(/:\s*$/, '');

    if (marker.startsWith('This page is for')) {
      return 'audience';
    }

    if (marker === 'Note') {
      return 'note';
    }

    if (marker === 'Warning') {
      return 'warning';
    }

    return null;
  }

  return null;
}

/**
 * A blockquote with Fuse styling when it carries a leading marker, otherwise a plain quote.
 */
function DocsBlockquote({ children, ...rest }: Readonly<ComponentProps<'blockquote'>>) {
  const kind = classifyBlockquote(children);

  if (kind === 'audience') {
    return <aside className="audience">{children}</aside>;
  }

  if (kind === 'note') {
    return <aside className="callout">{children}</aside>;
  }

  if (kind === 'warning') {
    return <aside className="callout warn">{children}</aside>;
  }

  return <blockquote {...rest}>{children}</blockquote>;
}

/**
 * One documentation page: crumbs, the Markdown body, an on-page table of contents, and the pager.
 *
 * The body renders with three Markdown overrides: links resolve through `resolveDocHref` so the sources can keep
 * linking by relative path, code blocks gain a copy button, and marked blockquotes render as Fuse audience and
 * callout boxes. Internal links use the router; anything else renders as a plain anchor.
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
    blockquote: (props: ComponentProps<'blockquote'>) => <DocsBlockquote {...props} />,
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
