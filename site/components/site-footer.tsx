import { companyName, companyUrl, repositoryUrl } from '@/lib/site';
import Link from 'next/link';

/**
 * Site footer shared by the landing, privacy, and documentation pages.
 *
 * Links the documentation index, the getting-started guide, the changelog, the
 * privacy statement, and the project repository, with the company attribution.
 */
export function SiteFooter() {
  return (
    <footer className="site-footer">
      <a className="maker" href={companyUrl}>
        A {companyName} project
      </a>
      <nav aria-label="Footer">
        <Link href="/docs">Docs</Link>
        <Link href="/docs/getting-started">Get Started</Link>
        <a href="https://github.com/litenova/LiteBus/blob/main/CHANGELOG.md">Changelog</a>
        <Link href="/privacy">Privacy</Link>
        <a href={repositoryUrl}>GitHub</a>
      </nav>
    </footer>
  );
}
