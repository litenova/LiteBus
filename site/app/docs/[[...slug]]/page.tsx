import type { Metadata } from 'next';
import { DocsArticle, docsMetadata } from '@/components/docs-article';
import { getSource } from '@/lib/source';
import { defaultVersion } from '@/lib/versions';

type DocsPageProps = {
  params: Promise<{ slug?: string[] }>;
};

export async function generateStaticParams() {
  return (await getSource(defaultVersion.id)).generateParams();
}

export async function generateMetadata({ params }: DocsPageProps): Promise<Metadata> {
  const { slug = [] } = await params;

  return docsMetadata(defaultVersion, slug);
}

export default async function DocsPageRoute({ params }: DocsPageProps) {
  const { slug = [] } = await params;

  return <DocsArticle version={defaultVersion} slug={slug} />;
}
