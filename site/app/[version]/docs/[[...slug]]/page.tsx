import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { DocsArticle, docsMetadata } from '@/components/docs-article';
import { getSource } from '@/lib/source';
import { findVersion, prefixedVersions } from '@/lib/versions';

type VersionedDocsPageProps = {
  params: Promise<{ version: string; slug?: string[] }>;
};

export const dynamicParams = false;

export async function generateStaticParams() {
  const perVersion = await Promise.all(
    prefixedVersions.map(async (version) => {
      const source = await getSource(version.id);

      return source.generateParams().map((params) => ({ ...params, version: version.id }));
    }),
  );

  return perVersion.flat();
}

export async function generateMetadata({ params }: VersionedDocsPageProps): Promise<Metadata> {
  const { version: versionId, slug = [] } = await params;
  const version = findVersion(versionId);

  if (version === undefined || version.default) {
    notFound();
  }

  return docsMetadata(version, slug);
}

export default async function VersionedDocsPageRoute({ params }: VersionedDocsPageProps) {
  const { version: versionId, slug = [] } = await params;
  const version = findVersion(versionId);

  if (version === undefined || version.default) {
    notFound();
  }

  return <DocsArticle version={version} slug={slug} />;
}
