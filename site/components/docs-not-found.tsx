'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { StatusBar } from '@/components/status-bar';
import { changelogUrl, companyName, docsBasePath, latestRelease } from '@/lib/site';

/**
 * The documentation not-found page.
 *
 * Renders in the docs chrome with the explorer from the shell and its own status bar, pointing at the documentation
 * index and the home page.
 */
export function DocsNotFound() {
  const pathname = usePathname();

  return (
    <>
      <main id="main" className="doc" tabIndex={-1}>
        <article className="prose">
          <h1>That page does not exist</h1>
          <p className="intro">
            <code>{pathname}</code> is not a documentation page.
          </p>
          <h2>Start from somewhere that exists</h2>
          <ul>
            <li>
              <Link href={docsBasePath}>Documentation index</Link>
            </li>
            <li>
              <Link href="/">Home</Link>
            </li>
          </ul>
        </article>
      </main>
      <StatusBar end={<span>{companyName}</span>}>
        <a href={changelogUrl} title="Changelog">
          v{latestRelease}
        </a>
      </StatusBar>
    </>
  );
}
