'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import {
  docsVersions,
  slugFromPathname,
  versionBasePath,
  versionFromPathname,
  versionHref,
} from '@/lib/versions';

/**
 * The documentation not-found page.
 *
 * Most requests that land here come from switching versions on a page that only one version has, so the page leads
 * with the same path in the other versions rather than only offering the index.
 */
export function DocsNotFound() {
  const pathname = usePathname();
  const version = versionFromPathname(pathname);
  const slug = slugFromPathname(pathname);
  const others = docsVersions.filter((candidate) => candidate.id !== version.id);

  return (
    <main className="litebus-docs-not-found">
      <h1>That page is not in {version.label}</h1>
      <p>
        <code>{pathname}</code> does not exist in the {version.label} documentation.
      </p>
      {slug.length > 0 ? (
        <>
          <h2>Try the same page in another version</h2>
          <ul>
            {others.map((candidate) => (
              <li key={candidate.id}>
                <Link href={versionHref(candidate, slug)}>
                  {candidate.label} - {candidate.description}
                </Link>
              </li>
            ))}
          </ul>
        </>
      ) : null}
      <h2>Start from a documentation index</h2>
      <ul>
        {docsVersions.map((candidate) => (
          <li key={candidate.id}>
            <Link href={versionBasePath(candidate)}>
              {candidate.label} - {candidate.description}
            </Link>
          </li>
        ))}
      </ul>
    </main>
  );
}
