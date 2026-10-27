import Link from 'next/link';
import { changelogUrl, companyName, companyUrl, nugetUrl, repositoryUrl } from '@/lib/site';

/**
 * Site footer shared by the landing, privacy, and documentation pages.
 *
 * Renders the mock footer: the license sentence with the maker link in one line, and the
 * documentation, changelog, NuGet, privacy, and repository links beside it.
 */
export function SiteFooter() {
  return (
    <footer className="foot">
      <div className="in">
        <span>
          LiteBus is MIT licensed.{' '}
          <a href={companyUrl}>Built by {companyName}</a>.
        </span>
        <nav aria-label="Footer">
          <Link href="/docs">Docs</Link>
          <Link href="/docs/getting-started">Get Started</Link>
          <a href={repositoryUrl}>GitHub</a>
          <a href={nugetUrl}>NuGet</a>
          <a href={changelogUrl}>Changelog</a>
          <Link href="/privacy">Privacy</Link>
        </nav>
      </div>
    </footer>
  );
}
