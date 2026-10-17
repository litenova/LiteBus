import Link from 'next/link';
import { GitHubMark, Logo } from '@/components/logo';
import { SearchBox } from '@/components/search-box';
import { ThemeToggle } from '@/components/theme-toggle';
import { nugetUrl, repositoryUrl } from '@/lib/site';

/**
 * Site header shared by the landing, privacy, and documentation pages.
 *
 * Renders the mock top bar: the brand link and navigation on the left, the search box in the
 * centre column, and NuGet, GitHub, and the theme toggle on the right. The current page is
 * marked with `aria-current="page"`. Sticky below 1600px, static inside the framed window.
 */
export function SiteHeader({ current }: Readonly<{ current?: 'home' | 'docs' }>) {
  return (
    <header className="top">
      <div className="top-l">
        <Link className="brand" href="/" aria-current={current === 'home' ? 'page' : undefined}>
          <Logo />
          LiteBus
        </Link>
        <nav className="nav" aria-label="Main">
          <Link href="/docs" aria-current={current === 'docs' ? 'page' : undefined}>
            Docs
          </Link>
          <Link href="/docs/getting-started">Get Started</Link>
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
