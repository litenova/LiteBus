import { notFound } from 'next/navigation';
import { DocsShell } from '@/components/docs-shell';
import { findVersion, prefixedVersions } from '@/lib/versions';

type VersionLayoutProps = {
  params: Promise<{ version: string }>;
  children: React.ReactNode;
};

export function generateStaticParams() {
  return prefixedVersions.map((version) => ({ version: version.id }));
}

// A segment that names no declared version is not a version at all, and rendering it would give every unmatched
// top-level path a documentation shell.
export const dynamicParams = false;

export default async function VersionedDocsLayout({ params, children }: VersionLayoutProps) {
  const version = findVersion((await params).version);

  if (version === undefined || version.default) {
    notFound();
  }

  return <DocsShell version={version}>{children}</DocsShell>;
}
