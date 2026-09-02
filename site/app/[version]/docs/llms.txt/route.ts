import { notFound } from 'next/navigation';
import { llmsIndex } from '@/lib/llms';
import { findVersion, prefixedVersions } from '@/lib/versions';

/**
 * The llms.txt index for a version served under its own prefix.
 *
 * The convention has a file cover the URLs beneath its own path, and a client is told to prefer the most specific file
 * that applies. A line documented at `/v7/docs` therefore indexes itself at `/v7/docs/llms.txt` rather than being
 * folded into the root index, which describes a different release.
 */

export const revalidate = false;

export function generateStaticParams() {
  return prefixedVersions.map((version) => ({ version: version.id }));
}

// A segment that names no declared version is not a version at all.
export const dynamicParams = false;

export async function GET(_request: Request, { params }: { params: Promise<{ version: string }> }) {
  const version = findVersion((await params).version);

  if (version === undefined || version.default) {
    notFound();
  }

  return new Response(await llmsIndex(version), {
    headers: {
      'Content-Type': 'text/plain; charset=utf-8',
    },
  });
}
