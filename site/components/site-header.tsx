'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { GitHubMark, Logo } from '@/components/logo';
import { SearchBox } from '@/components/search-box';
import { ThemeToggle } from '@/components/theme-toggle';
import { nugetUrl, repositoryUrl } from '@/lib/site';

/**
 * Site header shared by the landing, privacy, and documentation pages.
 *
 * Renders the mock top bar: the brand link and navigation on the left, the search box in the
 * centre column, and NuGet, GitHub, and the theme toggle on the right. The current page is
 * marked with `aria-current="page"`: the brand on the home page, Getting Started on its guide,
 * and Docs on the other documentation pages. Sticky below 1600px, static inside the framed
 * window.
 */
export function SiteHeader({ current }: Readonly<{ current?: 'home' | 'docs' }>) {
  const pathname = usePathname();
  const isHome = current === 'home' || pathname === '/';
  const isGettingStarted = pathname === '/docs/getting-started';
  const isDocs = (current === 'docs' || pathname?.startsWith('/docs') === true) && !isGettingStarted;

  return (
    <header className="top">
      <div className="top-l">
        <Link className="brand" href="/" aria-label="LiteBus home" aria-current={isHome ? 'page' : undefined}>
          <Logo />
          LiteBus
        </Link>
        <nav className="nav" aria-label="Main">
          <Link href="/docs" aria-current={isDocs ? 'page' : undefined}>
            Docs
          </Link>
          <Link href="/docs/getting-started" aria-current={isGettingStarted ? 'page' : undefined}>
            Getting Started
          </Link>
        </nav>
      </div>
      <SearchBox />
      <div className="top-r">
        <a className="hide-sm" href={nugetUrl}>
          NuGet
        </a>
        <a className="gh" href={repositoryUrl} aria-label="LiteBus on GitHub">
          <GitHubMark />
          <span className="hide-sm">GitHub</span>
        </a>
        <ThemeToggle />
      </div>
    </header>
  );
}
