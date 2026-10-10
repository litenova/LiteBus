import { notFound } from 'next/navigation';
import { llmsFull } from '@/lib/llms';
import { findVersion, prefixedVersions } from '@/lib/versions';

/** The complete text of a version served under its own prefix, as one document. */

export const revalidate = false;

export function generateStaticParams() {
  return prefixedVersions.map((version) => ({ version: version.id }));
}

export const dynamicParams = false;

export async function GET(_request: Request, { params }: { params: Promise<{ version: string }> }) {
  const version = findVersion((await params).version);

  if (version === undefined || version.default) {
    notFound();
  }

  return new Response(await llmsFull(version), {
    headers: {
      'Content-Type': 'text/plain; charset=utf-8',
    },
  });
}
