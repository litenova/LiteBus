import type { Metadata } from 'next';
import type { ComponentProps } from 'react';
import { notFound } from 'next/navigation';
import { DocsBody, DocsPage } from 'fumadocs-ui/layouts/docs/page';
import defaultMdxComponents from 'fumadocs-ui/mdx';
import { getSource } from '@/lib/source';
import { siteDescription, siteName } from '@/lib/site';

type DocsPageProps = {
  params: Promise<{ slug?: string[] }>;
};

export async function generateStaticParams() {
  return (await getSource()).generateParams();
}

export async function generateMetadata({ params }: DocsPageProps): Promise<Metadata> {
  const { slug = [] } = await params;
  const page = (await getSource()).getPage(slug);

  if (!page) {
    notFound();
  }

  const title = page.data.title ?? siteName;
  const description = page.data.description ?? siteDescription;

  return {
    title,
    description,
    alternates: {
      canonical: page.url,
    },
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

export default async function DocsPageRoute({
  params,
}: DocsPageProps) {
  const { slug = [] } = await params;
  const source = await getSource();
  const page = source.getPage(slug);

  if (!page) {
    notFound();
  }

  const renderer = await page.data.load();
  const DefaultLink = defaultMdxComponents.a;
  const rendered = await renderer.render({
    ...defaultMdxComponents,
    a: ({ href, ...props }: ComponentProps<'a'>) => {
      let resolvedHref = href;

      if (href !== undefined && /\.md(?:#|$)/i.test(href) && !/^[a-z]+:/i.test(href)) {
        const sourceHref = href.startsWith('.') ? href : `./${href}`;
        resolvedHref = source.resolveHref(sourceHref, page);

        if (resolvedHref === sourceHref) {
          const repositoryPage = new URL(
            page.path,
            'https://github.com/litenova/LiteBus/blob/main/site/content/docs/',
          );
          resolvedHref = new URL(href, repositoryPage).toString();
        }
      }

      return <DefaultLink href={resolvedHref} {...props} />;
    },
  });

  return (
    <DocsPage toc={rendered.toc}>
      <DocsBody>{rendered.body}</DocsBody>
    </DocsPage>
  );
}
