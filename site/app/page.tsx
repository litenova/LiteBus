import Image from 'next/image';
import Link from 'next/link';
import type { ReactNode } from 'react';
import {
  companyName,
  companyUrl,
  repositoryUrl,
  siteDescription,
  siteName,
  siteUrl,
} from '@/lib/site';

const structuredData = {
  '@context': 'https://schema.org',
  '@graph': [
    {
      '@type': 'Organization',
      '@id': `${siteUrl}/#organization`,
      name: companyName,
      url: companyUrl,
    },
    {
      '@type': 'WebSite',
      '@id': `${siteUrl}/#website`,
      url: siteUrl,
      name: siteName,
      description: siteDescription,
      inLanguage: 'en',
      publisher: { '@id': `${siteUrl}/#organization` },
    },
    {
      '@type': 'SoftwareSourceCode',
      '@id': `${siteUrl}/#software`,
      name: siteName,
      description: siteDescription,
      codeRepository: repositoryUrl,
      programmingLanguage: 'C#',
      runtimePlatform: '.NET',
      url: siteUrl,
      license: 'https://github.com/litenova/LiteBus/blob/main/LICENSE',
      author: { '@id': `${siteUrl}/#organization` },
    },
  ],
};

const KEYWORDS = new Set([
  'public',
  'private',
  'sealed',
  'record',
  'class',
  'var',
  'await',
  'return',
  'void',
  'using',
  'namespace',
  'new',
  'static',
  'async',
  'readonly',
  'this',
]);

const PRIMITIVES = new Set(['string', 'decimal', 'bool', 'int', 'long', 'object']);

function highlight(code: string): ReactNode[] {
  const pattern =
    /(\/\/[^\n]*)|("(?:[^"\\]|\\.)*")|([A-Za-z_][A-Za-z0-9_]*)|(\d[\d_.]*m?)|(\s+)|([^\s\w])/g;
  const nodes: ReactNode[] = [];
  let match: RegExpExecArray | null;
  let index = 0;

  while ((match = pattern.exec(code)) !== null) {
    const [full, comment, str, word, num] = match;
    const key = index++;

    if (comment) {
      nodes.push(
        <span key={key} className="tok-c">
          {comment}
        </span>,
      );
    } else if (str) {
      nodes.push(
        <span key={key} className="tok-s">
          {str}
        </span>,
      );
    } else if (word) {
      const cls = KEYWORDS.has(word)
        ? 'tok-k'
        : PRIMITIVES.has(word) || /^[A-Z]/.test(word)
          ? 'tok-t'
          : undefined;
      nodes.push(
        cls ? (
          <span key={key} className={cls}>
            {word}
          </span>
        ) : (
          <span key={key}>{word}</span>
        ),
      );
    } else if (num) {
      nodes.push(
        <span key={key} className="tok-n">
          {num}
        </span>,
      );
    } else {
      nodes.push(<span key={key}>{full}</span>);
    }
  }

  return nodes;
}

function CodeWindow({ label, code }: { label: string; code: string }) {
  return (
    <div className="litebus-code">
      <div className="litebus-code-bar">
        <span className="litebus-code-mark" aria-hidden="true" />
        <span className="litebus-code-label">{label}</span>
      </div>
      <pre>
        <code>{highlight(code)}</code>
      </pre>
    </div>
  );
}

const QUICKSTART_CODE = `builder.Services.AddLiteBus(bus =>
{
    bus.AddMessaging(_ => { });
    bus.AddCommands(commands =>
        commands.RegisterFromAssembly(typeof(Program).Assembly));
});

var id = await mediator.SendAsync(
    new CreateProduct("Widget", 9.99m),
    cancellationToken);`;

const CAPABILITIES = [
  {
    title: 'Commands and Queries',
    body: 'Separate command and query mediators encode write and read intent for CQS applications.',
    icon: <path d="M4 7h13m0 0-3-3m3 3-3 3M20 17H7m0 0 3-3m-3 3 3 3" />,
  },
  {
    title: 'Domain Events',
    body: 'Publish plain .NET event types to zero or more handlers without requiring a shared event base class.',
    icon: <path d="M8 18h8M10 21h4M6 15h12l-1.5-2.5V9a4.5 4.5 0 0 0-9 0v3.5L6 15Z" />,
  },
  {
    title: 'Handler Pipelines',
    body: 'Run validation and cross-cutting work through named pre-, post-, and error-handler stages.',
    icon: <path d="M4 6h5m6 0h5M9 4v4m6 3v4M4 13h11m0 0h5M4 20h5m6 0h5M9 18v4" />,
  },
  {
    title: 'Durable Inbox',
    body: 'Persist commands before deferred execution with leases, retries, receipts, and at-least-once delivery.',
    icon: <path d="M4 5h16v14H4V5Zm4 8h2l2 2 2-2h2M12 4v7m0 0-3-3m3 3 3-3" />,
  },
  {
    title: 'Transactional Outbox',
    body: 'Store events with domain state through transactional writers, then publish them with an outbox processor.',
    icon: <path d="M4 5h16v14H4V5Zm4 8h2l2 2 2-2h2M12 11V4m0 0-3 3m3-3 3 3" />,
  },
  {
    title: 'Saga State',
    body: 'Persist correlated state around inbox command dispatch with in-memory or PostgreSQL storage.',
    icon: <path d="M7 7a3 3 0 1 0 0 6 3 3 0 0 0 0-6Zm10 4a3 3 0 1 0 0 6 3 3 0 0 0 0-6ZM9.5 8.5l5-2M9.5 12l4.8 2" />,
  },
  {
    title: 'Storage and Transport',
    body: 'Select PostgreSQL, EF Core, in-memory, RabbitMQ, Kafka, AWS SQS, or Azure Service Bus through opt-in adapters.',
    icon: <path d="M4 6c0-1.1 3.6-2 8-2s8 .9 8 2-3.6 2-8 2-8-.9-8-2Zm0 0v6c0 1.1 3.6 2 8 2s8-.9 8-2V6M4 12v6c0 1.1 3.6 2 8 2s8-.9 8-2v-6" />,
  },
  {
    title: 'Hosting and Operations',
    body: 'Run processors through host adapters and add OpenTelemetry, health checks, management endpoints, and analyzers.',
    icon: <path d="M3 12h4l2.5-6 4 12 2.5-6h5M5 4h14M5 20h14" />,
  },
];

export default function HomePage() {
  return (
    <div className="litebus-home">
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: JSON.stringify(structuredData) }}
      />

      <header className="litebus-home-header">
        <div className="litebus-header-inner">
          <Link className="litebus-brand" href="/" aria-label="LiteBus home">
            <Image src="/icon.svg" alt="" width={30} height={30} priority />
            <span>LiteBus</span>
          </Link>
          <nav className="litebus-home-nav" aria-label="Main navigation">
            <Link href="/docs">Documentation</Link>
            <Link href="https://github.com/litenova/LiteBus">GitHub</Link>
            <Link className="nav-cta" href="/docs/getting-started">
              Get Started
            </Link>
          </nav>
        </div>
      </header>

      <main>
        <section className="litebus-hero">
          <div className="litebus-hero-copy">
            <div className="litebus-badges" aria-label="Project licensing">
              <span>MIT Licensed</span>
              <span>Open Source</span>
              <span>.NET 10</span>
            </div>
            <h1>Message Mediation and Durable Processing for .NET</h1>
            <p>
              LiteBus provides command, query, and event mediation for CQS and DDD applications,
              with optional inbox, outbox, saga, storage, and transport modules.
            </p>
            <div className="litebus-actions">
              <Link className="primary" href="/docs/getting-started">
                Getting Started
              </Link>
              <Link href="/docs">Documentation</Link>
            </div>
            <div className="litebus-install" aria-label="Install command">
              <span className="litebus-prompt" aria-hidden="true">
                $
              </span>
              <code>dotnet add package LiteBus.Extensions.Microsoft.DependencyInjection</code>
            </div>
          </div>

          <div className="litebus-hero-code">
            <CodeWindow label="Program.cs" code={QUICKSTART_CODE} />
          </div>
        </section>

        <section className="litebus-section litebus-capabilities" aria-labelledby="capabilities-heading">
          <div className="litebus-section-intro">
            <span className="litebus-section-label">Modules and Integrations</span>
            <h2 id="capabilities-heading">What LiteBus Includes</h2>
            <p>
              LiteBus separates mediation, durable processing, adapters, and operational
              integrations into independent packages.
            </p>
          </div>
          <div className="litebus-capability-list">
            {CAPABILITIES.map((capability) => (
              <article key={capability.title} className="litebus-capability">
                <span className="litebus-capability-icon" aria-hidden="true">
                  <svg
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="1.6"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  >
                    {capability.icon}
                  </svg>
                </span>
                <h3>{capability.title}</h3>
                <p>{capability.body}</p>
              </article>
            ))}
          </div>
        </section>

        <section className="litebus-mediatr" aria-labelledby="mediatr-heading">
          <div>
            <span className="litebus-section-label">MediatR Comparison</span>
            <h2 id="mediatr-heading">An MIT-Licensed Alternative to MediatR</h2>
          </div>
          <div className="litebus-mediatr-copy">
            <p>
              MediatR 13 moved to dual commercial and open-source licensing and requires a license
              key. LiteBus remains MIT licensed and free for commercial use, with a message model
              designed around CQS and DDD.
            </p>
            <Link href="/docs/migration/mediatr-differences">Compare LiteBus and MediatR</Link>
          </div>
        </section>
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
            <Link href="/docs/architecture">Architecture</Link>
            <Link href="/privacy">Privacy</Link>
            <Link href="https://github.com/litenova/LiteBus">GitHub</Link>
          </nav>
        </div>
      </footer>
    </div>
  );
}
