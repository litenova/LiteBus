import type { Metadata } from 'next';
import Image from 'next/image';
import Link from 'next/link';
import { companyName, companyUrl, repositoryUrl, siteName, siteUrl } from '@/lib/site';

export const metadata: Metadata = {
  title: 'Privacy statement',
  description: `How the ${siteName} documentation website handles personal data under the GDPR.`,
  alternates: {
    canonical: '/privacy',
  },
  openGraph: {
    type: 'article',
    url: '/privacy',
    title: `Privacy statement | ${siteName}`,
    description: `How the ${siteName} documentation website handles personal data under the GDPR.`,
  },
};

const LAST_UPDATED = '18 July 2026';

export default function PrivacyPage() {
  return (
    <div className="litebus-home">
      <header className="litebus-home-header">
        <div className="litebus-header-inner">
          <Link className="litebus-brand" href="/">
            <Image src="/icon.svg" alt="" width={32} height={32} />
            <span>LiteBus</span>
          </Link>
          <nav className="litebus-home-nav" aria-label="Main navigation">
            <Link href="/docs">Documentation</Link>
            <Link href="/docs/getting-started">Get Started</Link>
            <Link href={repositoryUrl}>GitHub</Link>
          </nav>
        </div>
      </header>

      <main>
        <article className="litebus-legal">
          <h1>Privacy statement</h1>
          <p className="litebus-legal-meta">Last updated: {LAST_UPDATED}</p>

          <p>
            This privacy statement explains how the {siteName} documentation website (
            {siteUrl}) handles personal data. It is written to meet the requirements of the
            General Data Protection Regulation (GDPR) and the Dutch implementation act (Uitvoeringswet
            AVG). {siteName} is an open source software project. This website provides its
            documentation and does not offer user accounts, comment forms, newsletters, e-commerce,
            or advertising.
          </p>

          <h2>Who is responsible</h2>
          <p>
            The controller for the processing described here is {companyName}, the company behind
            {' '}
            {siteName}. You can reach {companyName} through its website at{' '}
            <Link href={companyUrl}>{companyUrl.replace('https://', '')}</Link>. For any question
            about this statement or about your personal data, use the contact options listed there.
            Security issues can also be reported privately through the project on{' '}
            <Link href={repositoryUrl}>GitHub</Link>.
          </p>

          <h2>What data is processed</h2>
          <p>Visiting this website involves a small and limited amount of data.</p>
          <ul>
            <li>
              <strong>Server and delivery logs.</strong> Like every website, the hosting and content
              delivery provider that serves these pages records technical request data. This can
              include your IP address, the date and time of the request, the page or file requested,
              the referring page, and your browser and operating system identifiers. This data is
              used to deliver the site, keep it available, and protect it against abuse and attacks.
            </li>
            <li>
              <strong>Local storage for your display preference.</strong> When you switch between the
              light and dark theme, your choice is saved in your browser using local storage. This
              stays on your device, is not sent to any server, and is used only to remember your
              preference on your next visit.
            </li>
            <li>
              <strong>No tracking.</strong> This website sets no advertising cookies and no analytics
              cookies. It does not build visitor profiles, does not use third party trackers, and
              does not sell or share personal data.
            </li>
          </ul>

          <h2>Cookies and local storage</h2>
          <p>
            This website does not use cookies for tracking or analytics. The only client side storage
            is the theme preference described above. Under Article 11.7a of the Dutch
            Telecommunications Act and the ePrivacy rules, storage that is strictly necessary to
            provide a service the visitor asked for is exempt from prior consent, so no cookie banner
            is shown. If tracking or analytics are added later, this statement will be updated and
            consent will be requested first.
          </p>

          <h2>Legal basis</h2>
          <p>
            Server and delivery logs are processed on the basis of a legitimate interest under
            Article 6(1)(f) of the GDPR, namely delivering the website reliably and protecting it
            against misuse. The theme preference is stored on the basis that it is strictly necessary
            to provide the display setting you selected.
          </p>

          <h2>Who else is involved</h2>
          <p>
            The website is hosted and delivered by Vercel Inc., which acts as a processor and records
            the technical delivery logs described above on our behalf. Source code links and security
            reports point to GitHub, operated by GitHub, Inc. Both are based in the United States, so
            serving the site can involve a transfer of technical data outside the European Economic
            Area. Those transfers rely on the appropriate safeguards offered by the provider, in
            particular the European Commission standard contractual clauses and the provider data
            processing agreement. No personal data is shared with any other party for its own
            purposes.
          </p>

          <h2>How long data is kept</h2>
          <p>
            Server and delivery logs are retained by the hosting provider only for as long as needed
            for delivery, security, and abuse prevention, after which they are deleted or aggregated.
            The theme preference remains in your browser until you clear your browser storage.
          </p>

          <h2>Your rights</h2>
          <p>
            Under the GDPR you have the right to access your personal data, to have it corrected or
            erased, to restrict or object to its processing, and to data portability. To exercise any
            of these rights, contact {companyName} using the details listed above. Because this
            website keeps no accounts
            and stores no directly identifying records beyond provider level technical logs, the data
            that can be tied to you is limited.
          </p>

          <h2>Complaints</h2>
          <p>
            If you believe your personal data is handled incorrectly, you can lodge a complaint with
            the Dutch data protection authority, the Autoriteit Persoonsgegevens, at{' '}
            <Link href="https://www.autoriteitpersoonsgegevens.nl">
              autoriteitpersoonsgegevens.nl
            </Link>
            .
          </p>

          <h2>Changes to this statement</h2>
          <p>
            This statement may be updated when the website changes or when legal requirements change.
            The date at the top shows when it was last revised.
          </p>

          <p>
            <Link href="/">Back to home</Link>
          </p>
        </article>
      </main>

      <footer className="litebus-footer">
        <div className="litebus-footer-inner">
          <div>
            <Link className="litebus-brand" href="/">
              <Image src="/icon.svg" alt="" width={24} height={24} />
              <span>LiteBus</span>
            </Link>
            <p>
              A <Link href={companyUrl}>{companyName}</Link> project.
            </p>
          </div>
          <nav aria-label="Footer navigation">
            <Link href="/docs">Documentation</Link>
            <Link href="/docs/getting-started">Get Started</Link>
            <Link href="/privacy">Privacy</Link>
            <Link href={repositoryUrl}>GitHub</Link>
          </nav>
        </div>
      </footer>
    </div>
  );
}
