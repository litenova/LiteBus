import { changelogUrl, companyName, companyUrl, nugetUrl, repositoryUrl } from '@/lib/site';
import Image from 'next/image';
import Link from 'next/link';

/**
 * Site footer shared by the landing, privacy, and documentation pages.
 *
 * Renders the maker block with the Litenova mark, the documentation, changelog,
 * NuGet, privacy, and repository links, and the license sentence.
 */
export function SiteFooter() {
  return (
    <footer className="site-footer">
      <a className="maker" href={companyUrl}>
        <Image src="/litenova-mark.svg" alt="" width={27} height={27} />
        Built by {companyName}
      </a>
      <nav aria-label="Footer">
        <Link href="/docs">Docs</Link>
        <Link href="/docs/getting-started">Get Started</Link>
        <a href={changelogUrl}>Changelog</a>
        <a href={nugetUrl}>NuGet</a>
        <Link href="/privacy">Privacy</Link>
        <a href={repositoryUrl}>GitHub</a>
      </nav>
      <p className="license">
        LiteBus is MIT licensed, open source, free for commercial use, and will remain free.
      </p>
    </footer>
  );
}
