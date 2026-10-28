import type { Metadata } from 'next';
import { DocsArticle, docsMetadata } from '@/components/docs-article';
import { getSource } from '@/lib/source';

type DocsPageProps = {
  params: Promise<{ slug?: string[] }>;
};

export async function generateStaticParams() {
  return (await getSource()).generateParams();
}

export async function generateMetadata({ params }: DocsPageProps): Promise<Metadata> {
  const { slug = [] } = await params;

  return docsMetadata(slug);
}

export default async function DocsPageRoute({ params }: DocsPageProps) {
  const { slug = [] } = await params;

  return <DocsArticle slug={slug} />;
}
