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
        <span className="litebus-dots" aria-hidden="true">
          <i />
          <i />
          <i />
        </span>
        <span className="litebus-code-label">{label}</span>
      </div>
      <pre>
        <code>{highlight(code)}</code>
      </pre>
    </div>
  );
}

const REGISTER_CODE = `builder.Services.AddLiteBus(liteBus =>
{
    liteBus.AddMessaging(_ => { });
    liteBus.AddCommands(c => c.RegisterFromAssembly(assembly));
    liteBus.AddQueries(q => q.RegisterFromAssembly(assembly));
    liteBus.AddEvents(e => e.RegisterFromAssembly(assembly));
});`;

const CONTRACT_CODE = `public sealed record CreateProduct(string Name, decimal Price)
    : ICommand<Guid>;

public sealed class CreateProductHandler
    : ICommandHandler<CreateProduct, Guid>
{
    public Task<Guid> HandleAsync(CreateProduct command, CancellationToken ct)
        => Task.FromResult(Guid.NewGuid());
}`;

const DISPATCH_CODE = `// inject the mediator for the operation you are performing
var id = await mediator.SendAsync(
    new CreateProduct("Widget", 9.99m),
    cancellationToken);`;

type Feature = { title: string; body: string; icon: ReactNode };

const FEATURES: Feature[] = [
  {
    title: 'Semantic mediators',
    body: 'Commands, queries, and events keep separate contracts, each with its own focused handler pipeline.',
    icon: <path d="M4 6h7M4 12h16M4 18h10M15 4l3 3-3 3" />,
  },
  {
    title: 'Reliable messaging',
    body: 'Inbox, outbox, scheduling, retries, leases, and sagas compose as independent, opt-in concerns.',
    icon: <path d="M12 3l8 4v5c0 4.5-3.2 7.6-8 9-4.8-1.4-8-4.5-8-9V7l8-4z" />,
  },
  {
    title: 'Opt-in packaging',
    body: 'Reference a broker or ORM SDK only when you select it. No hidden transitive dependencies enter your graph.',
    icon: <path d="M12 3l8 4.5v9L12 21l-8-4.5v-9L12 3zM12 12l8-4.5M12 12v9M12 12L4 7.5" />,
  },
  {
    title: 'Roslyn analyzers',
    body: 'Diagnostics LB1001 through LB1017 catch handler and registration mistakes at compile time.',
    icon: <path d="M12 3l2.6 5.3 5.9.9-4.3 4.1 1 5.8L12 16.9 6.8 19.1l1-5.8-4.3-4.1 5.9-.9L12 3z" />,
  },
  {
    title: 'Built-in observability',
    body: 'OpenTelemetry meters and traces for inbox, outbox, and transport register through each host adapter.',
    icon: <path d="M3 12h4l3 7 4-14 3 7h4" />,
  },
  {
    title: 'Validated module graph',
    body: 'The module registry resolves and validates the full dependency graph before build. Callback order never matters.',
    icon: (
      <path d="M6 4a2 2 0 100 4 2 2 0 000-4zM18 16a2 2 0 100 4 2 2 0 000-4zM18 4a2 2 0 100 4 2 2 0 000-4zM7 8v3a3 3 0 003 3h6M18 8v6" />
    ),
  },
];

const INTEGRATIONS: { group: string; items: string[] }[] = [
  { group: 'Transport', items: ['AMQP / RabbitMQ', 'Apache Kafka', 'AWS SQS', 'Azure Service Bus'] },
  { group: 'Storage', items: ['PostgreSQL', 'Entity Framework Core', 'In-memory'] },
  { group: 'Hosting & telemetry', items: ['ASP.NET Core', 'OpenTelemetry', 'Health checks'] },
];

const STATS: { value: string; label: string }[] = [
  { value: '.NET 10', label: 'Target framework' },
  { value: 'Commands, Queries, Events', label: 'Separate contracts' },
  { value: 'Inbox + Outbox', label: 'Durable messaging' },
  { value: 'Opt-in', label: 'No hidden SDKs' },
];

export default function HomePage() {
  return (
    <div className="litebus-home">
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: JSON.stringify(structuredData) }}
      />
      <header className="litebus-home-header">
        <Link className="litebus-brand" href="/">
          <Image src="/icon.svg" alt="" width={32} height={32} />
          <span>LiteBus</span>
        </Link>
        <nav className="litebus-home-nav" aria-label="Main navigation">
          <Link href="/docs">Documentation</Link>
          <Link href="/docs/getting-started">Get started</Link>
          <Link href="https://github.com/litenova/LiteBus">GitHub</Link>
        </nav>
      </header>

      <main>
        <section className="litebus-hero">
          <div className="litebus-hero-copy">
            <div className="litebus-eyebrow">
              <span className="litebus-pill">v6.0</span>
              Mediator &amp; durable messaging for .NET
            </div>
            <h1>Clean message pipelines without hidden coupling.</h1>
            <p>
              Compose commands, queries, events, inbox, and outbox processing from explicit
              contracts and opt-in adapters. Install only the capabilities your service runs.
            </p>
            <div className="litebus-actions">
              <Link className="primary" href="/docs/getting-started">
                Get started
              </Link>
              <Link href="/docs">Read the documentation</Link>
            </div>
            <div className="litebus-install-chip" aria-label="Install command">
              <span className="litebus-prompt" aria-hidden="true">
                $
              </span>
              dotnet add package LiteBus.Commands.Extensions.Microsoft.DependencyInjection
            </div>
          </div>

          <div className="litebus-hero-code">
            <CodeWindow label="Program.cs" code={REGISTER_CODE} />
          </div>
        </section>

        <section className="litebus-stats" aria-label="At a glance">
          {STATS.map((stat) => (
            <div key={stat.label} className="litebus-stat">
              <span className="litebus-stat-value">{stat.value}</span>
              <span className="litebus-stat-label">{stat.label}</span>
            </div>
          ))}
        </section>

        <section className="litebus-section" aria-labelledby="features-heading">
          <div className="litebus-section-head">
            <h2 id="features-heading">Built from independent concerns</h2>
            <p>
              Each capability ships as its own package and stays out of the graphs that do not use
              it. Add exactly what a service needs and nothing more.
            </p>
          </div>
          <div className="litebus-features">
            {FEATURES.map((feature) => (
              <article key={feature.title} className="litebus-feature">
                <span className="litebus-feature-icon" aria-hidden="true">
                  <svg
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="1.6"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  >
                    {feature.icon}
                  </svg>
                </span>
                <h3>{feature.title}</h3>
                <p>{feature.body}</p>
              </article>
            ))}
          </div>
        </section>

        <section className="litebus-section litebus-showcase" aria-labelledby="showcase-heading">
          <div className="litebus-section-head">
            <h2 id="showcase-heading">From contract to dispatch</h2>
            <p>
              Define a message and one handler, then send it through the mediator built for that
              operation. The same shape scales to queries, events, and durable processing.
            </p>
          </div>
          <div className="litebus-showcase-grid">
            <CodeWindow label="Contracts.cs" code={CONTRACT_CODE} />
            <CodeWindow label="Usage.cs" code={DISPATCH_CODE} />
          </div>
        </section>

        <section className="litebus-section" aria-labelledby="integrations-heading">
          <div className="litebus-section-head">
            <h2 id="integrations-heading">Opt-in integrations</h2>
            <p>
              Brokers, stores, hosts, and observability adapters install on demand. An external SDK
              enters your application only when you select that integration.
            </p>
          </div>
          <div className="litebus-integrations">
            {INTEGRATIONS.map((column) => (
              <div key={column.group} className="litebus-integration-col">
                <span className="litebus-integration-group">{column.group}</span>
                <ul>
                  {column.items.map((item) => (
                    <li key={item}>{item}</li>
                  ))}
                </ul>
              </div>
            ))}
          </div>
        </section>

        <section className="litebus-cta" aria-labelledby="cta-heading">
          <h2 id="cta-heading">Start with the capability you need today.</h2>
          <p>
            Install one package, register it in a single callback, and grow the graph deliberately.
          </p>
          <div className="litebus-actions">
            <Link className="primary" href="/docs/getting-started">
              Get started
            </Link>
            <Link href="https://github.com/litenova/LiteBus">View on GitHub</Link>
          </div>
        </section>
      </main>

      <footer className="litebus-footer">
        <div className="litebus-brand">
          <Image src="/icon.svg" alt="" width={24} height={24} />
          <span>LiteBus</span>
        </div>
        <nav aria-label="Footer navigation">
          <Link href="/docs">Documentation</Link>
          <Link href="/docs/getting-started">Get started</Link>
          <Link href="/docs/architecture">Architecture</Link>
          <Link href="/privacy">Privacy</Link>
          <Link href="https://github.com/litenova/LiteBus">GitHub</Link>
        </nav>
        <span className="litebus-footer-note">
          A <Link href={companyUrl}>{companyName}</Link> project.
        </span>
      </footer>
    </div>
  );
}
