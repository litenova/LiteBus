import type { Metadata } from 'next';
import type { ComponentProps, ReactNode } from 'react';
import { Children, Fragment, isValidElement } from 'react';
import Link from 'next/link';
import { notFound } from 'next/navigation';
import type { Node } from 'fumadocs-core/page-tree';
import { CodeCopyButton } from '@/components/code-copy-button';
import { DocsOutline } from '@/components/docs-outline';
import { StatusBar } from '@/components/status-bar';
import { repositorySourcesUrl, resolveDocHref } from '@/lib/doc-links';
import { getSource } from '@/lib/source';
import {
  changelogUrl,
  companyName,
  docsBasePath,
  latestRelease,
  siteDescription,
  siteName,
} from '@/lib/site';

/**
 * Metadata for one documentation page.
 *
 * The tab title is the page name with its top-level section, so two pages that share a name in different sections stay
 * distinct; a section landing page and the documentation root show the page name alone. The layout template appends the
 * site name, while the Open Graph and Twitter titles keep this string unchanged.
 */
export async function docsMetadata(slug: string[]): Promise<Metadata> {
  const source = await getSource();
  const page = source.getPage(slug);

  if (!page) {
    notFound();
  }

  const title = page.data.title ?? siteName;
  const section = sectionFor(source.getPageTree().children, page.url);
  const fullTitle =
    slug.length === 0 || section === undefined || page.url === section.indexUrl || title === section.name
      ? title
      : `${title} - ${section.name}`;
  const description = page.data.description ?? siteDescription;

  return {
    title: fullTitle,
    description,
    alternates: {
      canonical: page.url,
    },
    openGraph: {
      type: 'article',
      url: page.url,
      title: fullTitle,
      description,
    },
    twitter: {
      card: 'summary_large_image',
      title: fullTitle,
      description,
    },
  };
}

/**
 * The top-level section holding a page: its display name and its landing page URL, when it has one.
 *
 * A section without an index page, such as the catalog, has a name but no landing URL. A page the tree does not reach
 * has no section, and its tab title shows the page name alone.
 */
function sectionFor(
  nodes: readonly Node[],
  url: string,
): { readonly name: string; readonly indexUrl: string | undefined } | undefined {
  for (const node of nodes) {
    if (node.type !== 'folder') {
      continue;
    }

    const name = nodeText(node.name, '');

    if (name.length === 0) {
      continue;
    }

    const holds = flattenPages([node], []).some((entry) => entry.url === url);

    if (holds) {
      return { name, indexUrl: node.index?.url };
    }
  }

  return undefined;
}

/**
 * One breadcrumb segment: the display name and the landing page URL, when the segment has one.
 */
type Crumb = {
  readonly name: string;
  readonly url: string | undefined;
};

/**
 * One page in navigation order, with the folders above it for the breadcrumb.
 */
type FlatPage = {
  readonly name: string;
  readonly url: string;
  readonly folders: readonly Crumb[];
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
function flattenPages(nodes: readonly Node[], folders: readonly Crumb[]): FlatPage[] {
  const pages: FlatPage[] = [];

  for (const node of nodes) {
    if (node.type === 'page') {
      pages.push({ name: nodeText(node.name, node.url), url: node.url, folders });
    } else if (node.type === 'folder') {
      const name = nodeText(node.name, '');
      const below = name.length > 0 ? [...folders, { name, url: node.index?.url }] : [...folders];

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
 * A folder index page usually repeats its folder's name, so a repeated segment is shown once. Each segment keeps the
 * landing page URL of the folder or page it names, so segments with a landing page can link to it.
 */
function crumbsFor(page: FlatPage): readonly Crumb[] {
  const segments: Crumb[] = [...page.folders, { name: page.name, url: page.url }];

  return segments.filter(
    (segment, index) => index === 0 || segment.name !== segments[index - 1].name,
  );
}

/**
 * One breadcrumb segment: a link when an earlier segment has a landing page, otherwise plain text.
 *
 * The last segment is the page being read, so it is marked current instead of linking to itself.
 */
function CrumbSegment({ segment, last }: Readonly<{ segment: Crumb; last: boolean }>) {
  if (last) {
    return <span aria-current="page">{segment.name}</span>;
  }

  if (segment.url === undefined) {
    return segment.name;
  }

  return <Link href={segment.url}>{segment.name}</Link>;
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
 * The box a blockquote maps to, decided by its first paragraph's leading marker.
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
 * A blockquote with callout styling when it carries a leading marker, otherwise a plain quote.
 *
 * The audience box and notes share the callout look; warnings keep the same box with the warning colour on the
 * left rule.
 */
function DocsBlockquote({ children, ...rest }: Readonly<ComponentProps<'blockquote'>>) {
  const kind = classifyBlockquote(children);

  if (kind === 'warning') {
    return <aside className="callout warn">{children}</aside>;
  }

  if (kind === 'audience' || kind === 'note') {
    return <aside className="callout">{children}</aside>;
  }

  return <blockquote {...rest}>{children}</blockquote>;
}

/**
 * The display label for a fence language tag.
 *
 * The listed languages map to fixed labels; anything else shows the tag with an initial capital, and a bare fence
 * shows `Text`.
 */
function languageLabel(tag: string): string {
  switch (tag.toLowerCase()) {
    case 'csharp':
    case 'cs':
      return 'C#';
    case 'bash':
    case 'sh':
      return 'Bash';
    case 'json':
      return 'JSON';
    case 'xml':
      return 'XML';
    case 'powershell':
    case 'pwsh':
      return 'PowerShell';
    case 'sql':
      return 'SQL';
    case 'yaml':
    case 'yml':
      return 'YAML';
    case '':
    case 'text':
    case 'plaintext':
      return 'Text';
    default:
      return tag.slice(0, 1).toUpperCase() + tag.slice(1);
  }
}

/**
 * The `pre` class list with the `code` class present.
 *
 * The syntax styles hang off `pre.code`, so a block that arrives without it gains it here rather than rendering
 * unstyled.
 */
function codeClassName(className: string | undefined): string {
  if (className === undefined || className.length === 0) {
    return 'code';
  }

  return className.split(' ').includes('code') ? className : `code ${className}`;
}

/**
 * One documentation page: the file tab, crumbs, the Markdown body, the outline, the pager, and the status bar.
 *
 * The body renders with Markdown overrides: links resolve through `resolveDocHref` so the sources can keep linking
 * by relative path, headings gain hover anchors, tables wrap for scrolling, code blocks gain a labelled head with a
 * copy button, and marked blockquotes render as callout boxes. Internal links use the router; anything else renders
 * as a plain anchor.
 */
export async function DocsArticle({ slug }: Readonly<{ slug: string[] }>) {
  const source = await getSource();
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

      const resolved = resolveDocHref(href, page, source);

      if (resolved.startsWith('/')) {
        return <Link href={resolved} {...props} />;
      }

      return <a href={resolved} {...props} />;
    },
    h2: ({ id, children, ...props }: ComponentProps<'h2'>) => {
      const headingText = textContent(children).trim();
      const label =
        headingText.length > 0 ? `Link to section ${headingText}` : `Link to section ${id ?? ''}`;

      return (
        <h2 id={id} {...props}>
          {children}
          {typeof id === 'string' && id.length > 0 ? (
            <a className="anchor" href={`#${id}`} aria-label={label}>
              <span aria-hidden="true">#</span>
            </a>
          ) : null}
        </h2>
      );
    },
    table: (props: ComponentProps<'table'>) => (
      <div className="tbl">
        <table {...props} />
      </div>
    ),
    pre: (props: ComponentProps<'pre'>) => {
      const lang = (props as Record<string, unknown>)['data-lang'];
      const title = typeof props.title === 'string' && props.title.length > 0 ? props.title : undefined;

      return (
        <div className="block">
          <div className="block-head">
            <span>{title ?? languageLabel(typeof lang === 'string' ? lang : '')}</span>
            <CodeCopyButton />
          </div>
          <pre {...props} className={codeClassName(props.className)} style={undefined} title={undefined} />
        </div>
      );
    },
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
  const pathSegments = page.path.split('/');
  const fileName = pathSegments[pathSegments.length - 1] ?? 'README.md';
  const editUrl = `${repositorySourcesUrl()}${page.path}`;

  return (
    <>
      <main id="main" className="doc" tabIndex={-1}>
        <div className="ed-tabs">
          <span className="ed-tab on">
            {fileName}
            <span className="x" aria-hidden="true">
              x
            </span>
          </span>
        </div>
        <p className="crumbs">
          {slug.length === 0 ? (
            'Docs'
          ) : (
            <>
              <Link href={docsBasePath}>Docs</Link>
              {crumbs.map((segment, index) => (
                <Fragment key={`${segment.name}-${index}`}>
                  <b>&gt;</b>
                  <CrumbSegment segment={segment} last={index === crumbs.length - 1} />
                </Fragment>
              ))}
            </>
          )}
        </p>
        <article className="prose">
          {rendered.body}
          {previous === undefined && next === undefined ? null : (
            <nav className="pager" aria-label="Previous and next page">
              {previous === undefined ? (
                <span className="pager-gap" aria-hidden="true" />
              ) : (
                <Link href={previous.url}>
                  <small>Previous</small>
                  {previous.name}
                </Link>
              )}
              {next === undefined ? null : (
                <Link href={next.url}>
                  <small>Next</small>
                  {next.name}
                </Link>
              )}
            </nav>
          )}
        </article>
      </main>
      <aside className="outline" aria-label="On this page">
        <div className="sticky">
          <h2 className="xhead">On This Page</h2>
          <DocsOutline headings={headings.map((entry) => ({ url: entry.url, title: entry.title }))} />
          <a className="edit" href={editUrl}>
            Edit this page on GitHub
          </a>
        </div>
      </aside>
      <StatusBar
        end={
          <>
            <a className="hide-sm" href={editUrl}>
              Edit this page on GitHub
            </a>
            <span>{companyName}</span>
          </>
        }
      >
        <a href={changelogUrl} title="Changelog">
          v{latestRelease}
        </a>
        <span className="hide-sm">{page.path}</span>
      </StatusBar>
    </>
  );
}
