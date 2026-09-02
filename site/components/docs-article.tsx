import type { Metadata } from 'next';
import type { ComponentProps } from 'react';
import Link from 'next/link';
import { notFound } from 'next/navigation';
import { DocsBody, DocsPage } from 'fumadocs-ui/layouts/docs/page';
import defaultMdxComponents from 'fumadocs-ui/mdx';
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
  const DefaultLink = defaultMdxComponents.a;
  const rendered = await renderer.render({
    ...defaultMdxComponents,
    a: ({ href, ...props }: ComponentProps<'a'>) => (
      <DefaultLink
        href={href === undefined ? href : resolveDocHref(href, page, source, version)}
        {...props}
      />
    ),
  });

  return (
    <DocsPage toc={rendered.toc}>
      <DocsBody>
        {version.status === 'prerelease' ? (
          <aside className="litebus-version-notice">
            <strong>{version.label} is a pre-release.</strong> This page documents{' '}
            <code>{version.release}</code>, which ships to NuGet as a pre-release package and may still change.{' '}
            <Link href={versionBasePath(defaultVersion)}>
              Read the {defaultVersion.label} documentation
            </Link>{' '}
            for the version most applications should install.
          </aside>
        ) : null}
        {rendered.body}
      </DocsBody>
    </DocsPage>
  );
}
