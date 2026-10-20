import { docsContentDir, repositoryUrl, sourceRef } from '@/lib/site';
import type { DocsSource } from '@/lib/source';

/**
 * Resolution of the Markdown-relative links the documentation sources are written with.
 *
 * Pages link to each other by source path (`../getting-started/README.md`) so that the Markdown stays readable in the
 * repository and on GitHub. Every consumer of the content has to turn those into real URLs: the rendered page, the
 * `.md` endpoints, and the concatenated `llms-full.txt`. They all resolve them the same way here, which is what keeps
 * `scripts/check-rendered-links.mjs` from being a gate on the renderer alone.
 */

/** A page as returned by the loader. */
export type DocsPage = NonNullable<ReturnType<DocsSource['getPage']>>;

/** Matches a link that points at a Markdown source file, with or without a fragment. */
const markdownHrefPattern = /\.md(?:#|$)/i;

/** Matches a link that already names a scheme, including `mailto:` and protocol-relative forms. */
const absoluteHrefPattern = /^(?:[a-z][a-z0-9+.-]*:|\/\/)/i;

/** Where the Markdown sources are browsable, used for links that resolve to no page on the site. */
export function repositorySourcesUrl(): string {
  return `${repositoryUrl}/blob/${sourceRef}/site/${docsContentDir}/`;
}

/**
 * The URL a link in a documentation page should point at.
 *
 * A link to a page the site serves becomes that page's path. A link to Markdown the site does not serve, such as a
 * file outside the content root, falls back to the sources in the repository at the ref the documentation is built
 * from. Anything that is not a relative Markdown path is returned untouched.
 */
export function resolveDocHref(href: string, page: DocsPage, source: DocsSource): string {
  if (!markdownHrefPattern.test(href) || absoluteHrefPattern.test(href)) {
    return href;
  }

  // `resolveHref` only recognizes an explicitly relative path, while the sources also use bare sibling names such as
  // `capability-catalog.md`.
  const sourceHref = href.startsWith('.') ? href : `./${href}`;
  const resolved = source.resolveHref(sourceHref, page);

  if (resolved !== sourceHref) {
    return resolved;
  }

  return new URL(href, new URL(page.path, repositorySourcesUrl())).toString();
}

/**
 * The `.md` endpoint for a documentation path, keeping any fragment after the extension.
 *
 * Agents that follow a link out of `llms.txt` should land on Markdown rather than on the rendered shell, so the
 * generated indexes link to these instead of to the page URLs.
 */
export function markdownHref(href: string): string {
  const fragmentStart = href.indexOf('#');
  const path = fragmentStart === -1 ? href : href.slice(0, fragmentStart);
  const fragment = fragmentStart === -1 ? '' : href.slice(fragmentStart);

  return `${path}.md${fragment}`;
}
