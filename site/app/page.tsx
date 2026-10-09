import { CopyButton } from '@/components/copy-button';
import { SiteFooter } from '@/components/site-footer';
import { SiteHeader } from '@/components/site-header';
import {
  changelogUrl,
  companyName,
  companyUrl,
  repositoryUrl,
  siteDescription,
  siteName,
  siteUrl,
} from '@/lib/site';
import { defaultVersion, docsVersions, versionBasePath } from '@/lib/versions';
import Link from 'next/link';

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
      offers: { '@type': 'Offer', price: '0', priceCurrency: 'USD' },
      author: { '@id': `${siteUrl}/#organization` },
    },
  ],
};

const QUICKSTART_CODE = `builder.Services.AddLiteBus(bus =>
{
    bus.AddMessaging(_ => { });
    bus.AddCommands(commands =>
        commands.RegisterFromAssembly(typeof(Program).Assembly));
});

var mediator = app.Services.GetRequiredService<ICommandMediator>();
var id = await mediator.SendAsync(
    new CreateProductCommand("Widget", 9.99m),
    cancellationToken);`;

const HANDLER_CODE = `public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    private readonly IProductRepository _repository;

    public CreateProductCommandHandler(IProductRepository repository)
    {
        _repository = repository;
    }

    public async Task<Guid> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        return await _repository.AddAsync(command.Name, command.Price, cancellationToken);
    }
}`;

const INSTALL_COMMAND = 'dotnet add package LiteBus.Extensions.Microsoft.DependencyInjection';

/**
 * Landing page for the LiteBus site.
 *
 * Presents the hero with the current pre-release and the stable line, the
 * two-pane mediation sample, the durable messaging spotlight, the feature
 * cards, the package table, the MediatR comparison, and the boundary line.
 */
export default function HomePage() {
  const prerelease = docsVersions.find((version) => version.status === 'prerelease') ?? defaultVersion;
  const stable = docsVersions.find((version) => version.status === 'stable') ?? defaultVersion;

  return (
    <>
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: JSON.stringify(structuredData) }}
      />
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      <div className="wrap">
        <SiteHeader current="home" />
        <main id="main" tabIndex={-1}>
          <section className="hero">
            <a className="release" href={changelogUrl}>
              <b>v{prerelease.release}</b>Read what changed
            </a>
            <h1>Command, Query, and Event Mediation for .NET</h1>
            <ul className="tags" aria-label="Platform and license">
              <li>.NET 10</li>
              <li>C#</li>
              <li>MIT Licensed</li>
              <li>Open Source</li>
              <li>Opt-in Packages</li>
            </ul>
            <p>
              LiteBus sends commands, runs queries, and publishes events through handler pipelines
              in your process. Add the inbox, outbox, and saga modules when a message has to
              survive a restart.
            </p>
            <div className="install">
              <p className="label">Install the package:</p>
              <div className="cmd">
                <span>
                  <span className="dim">$ </span>
                  {INSTALL_COMMAND}
                </span>
                <CopyButton text={INSTALL_COMMAND} />
              </div>
              <p className="label">
                New to LiteBus? <Link href="/docs/getting-started">Get Started</Link> sends your first
                command, query, and event.
              </p>
              <p className="label">
                Stable release {stable.release}: read the{' '}
                <Link href={versionBasePath(stable)}>{stable.label} documentation</Link>.
              </p>
            </div>
          </section>

          <section className="section" aria-labelledby="one-message-one-handler-heading">
            <div className="section-head">
              <p className="kicker">In Your Code</p>
              <h2 id="one-message-one-handler-heading">One Message, One Handler</h2>
              <p>Register handlers from an assembly, then send a command.</p>
            </div>
            <div className="pair">
              <pre data-label="Program.cs">
                <code>{QUICKSTART_CODE}</code>
              </pre>
              <pre data-label="CreateProductCommandHandler.cs">
                <code>{HANDLER_CODE}</code>
              </pre>
            </div>
            <p className="proof">
              Handlers run in the caller&apos;s process. Nothing is persisted until you add a store.
            </p>
          </section>

          <section className="section" aria-labelledby="durable-inbox-and-transactional-outbox-heading">
            <div className="section-head">
              <p className="kicker">When a Message Must Not Be Lost</p>
              <h2 id="durable-inbox-and-transactional-outbox-heading">
                Durable Inbox and Transactional Outbox
              </h2>
              <p>Persist the message first, then execute or publish it under a lease with retries.</p>
            </div>
            <div className="spot">
              <div className="spot-text">
                <p>
                  The outbox writes the event in the same transaction as your data, and a processor
                  publishes it after commit. The inbox accepts a command durably and executes it
                  under a lease with retries.
                </p>
                <ul className="spot-stats">
                  <li>
                    <b>5</b>transports: RabbitMQ over AMQP, Kafka, AWS SQS, Azure Service Bus, and
                    in-memory.
                  </li>
                  <li>
                    <b>3</b>store families: PostgreSQL, EF Core, and in-memory. EF Core storage is
                    tested on PostgreSQL, SQL Server, MySQL, and SQLite.
                  </li>
                </ul>
                <p className="spot-links">
                  <Link href="/docs/reliable-messaging/outbox">How the outbox works {'->'}</Link>
                  <Link href="/docs/reliable-messaging/semantics">Delivery guarantees {'->'}</Link>
                </p>
              </div>
              <div className="spot-diagram" aria-hidden="true">
                <div className="box">
                  <b>Your transaction</b>
                  <span>domain data and event commit together</span>
                </div>
                <div className="join single"></div>
                <div className="box">
                  <b>Outbox store</b>
                  <span>PostgreSQL, EF Core, or in-memory</span>
                </div>
                <div className="join single"></div>
                <div className="box engine">
                  <b>Processor</b>
                  <span>leases each envelope with retries</span>
                </div>
                <div className="join single"></div>
                <div className="box">
                  <b>Broker</b>
                  <span>AMQP, Kafka, SQS, or Azure Service Bus</span>
                </div>
              </div>
            </div>
            <ul className="features">
              <li>
                <Link href="/docs/concepts/handler-pipeline">
                  <b>Handler Pipelines</b>
                  <span>Run validation and cross-cutting work through named pre-, post-, and error-handler stages.</span>
                  <i>Read the pipeline guide {'->'}</i>
                </Link>
              </li>
              <li>
                <Link href="/docs/concepts/handler-filtering">
                  <b>Tags and Filters</b>
                  <span>Run a different set of handlers per call with compile-time tags or a publish-time predicate.</span>
                  <i>Read the filtering guide {'->'}</i>
                </Link>
              </li>
              <li>
                <Link href="/docs/reliable-messaging/saga">
                  <b>Saga State</b>
                  <span>Persist correlated state around inbox command dispatch with in-memory or PostgreSQL storage.</span>
                  <i>Read the saga guide {'->'}</i>
                </Link>
              </li>
              <li>
                <Link href="/docs/reference/analyzers">
                  <b>20 Analyzer Rules</b>
                  <span>Catch handler registration mistakes, durable contract gaps, and inbox misuse at compile time.</span>
                  <i>Read the analyzer reference {'->'}</i>
                </Link>
              </li>
              <li>
                <Link href="/docs/operations/diagnostics-and-health">
                  <b>OpenTelemetry and Health</b>
                  <span>Bridge framework-neutral diagnostic probes to host health checks, metrics, and traces.</span>
                  <i>Read the operations guide {'->'}</i>
                </Link>
              </li>
              <li>
                <Link href="/docs/catalog/hosting/aspnet-management-endpoints">
                  <b>Management Endpoints</b>
                  <span>Map authenticated HTTP routes for inbox, outbox, processor control, retention, and diagnostics.</span>
                  <i>Read the endpoint reference {'->'}</i>
                </Link>
              </li>
            </ul>
          </section>

          <section className="section" aria-labelledby="install-only-what-you-run-heading">
            <div className="section-head">
              <p className="kicker">Packages</p>
              <h2 id="install-only-what-you-run-heading">Install Only What You Run</h2>
              <p>Reference only the adapters your host runs.</p>
            </div>
            <div className="table-scroll">
              <table>
                <thead>
                  <tr>
                    <th>Need</th>
                    <th>Packages</th>
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    <td>Mediation only</td>
                    <td>
                      <code>LiteBus.Extensions.Microsoft.DependencyInjection</code>
                    </td>
                  </tr>
                  <tr>
                    <td>Inbox with PostgreSQL</td>
                    <td>
                      <code>LiteBus.Inbox.Storage.PostgreSql</code>, <code>LiteBus.Inbox.Dispatch.InProcess</code>
                    </td>
                  </tr>
                  <tr>
                    <td>Outbox with in-process dispatch</td>
                    <td>
                      <code>LiteBus.Outbox.Storage.InMemory</code>, <code>LiteBus.Outbox.Dispatch.InProcess</code>
                    </td>
                  </tr>
                  <tr>
                    <td>Outbox to RabbitMQ</td>
                    <td>
                      <code>LiteBus.Transport.Amqp</code>, <code>LiteBus.Outbox.Dispatch.Amqp</code>
                    </td>
                  </tr>
                  <tr>
                    <td>OpenTelemetry</td>
                    <td>
                      <code>LiteBus.Messaging.Extensions.OpenTelemetry</code>, <code>LiteBus.Inbox.Extensions.OpenTelemetry</code>, <code>LiteBus.Outbox.Extensions.OpenTelemetry</code>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
            <p className="proof">
              Every package and its role: <Link href="/docs/architecture/dependency-graph">Dependency Graph</Link>.
            </p>
          </section>

          <section className="section" aria-labelledby="moving-from-mediatr-heading">
            <div className="section-head">
              <p className="kicker">Migration</p>
              <h2 id="moving-from-mediatr-heading">Moving From MediatR</h2>
            </div>
            <p>
              MediatR 13.0.0 and later ships under a commercial license, with a free Community License
              for organizations below USD 5 million in annual gross revenue; earlier versions remain
              under Apache 2.0 or MIT. (<a href="https://mediatr.io/">mediatr.io</a>, retrieved 2026-10-09)
            </p>
            <p>LiteBus is MIT licensed, open source, free for commercial use, and will remain free.</p>
            <p>
              <Link href="/docs/migration/mediatr-differences">Compare LiteBus and MediatR {'->'}</Link>
            </p>
          </section>

          <p className="proof">
            <b>The boundary:</b> mediation alone persists nothing. Durable delivery needs a store
            adapter and a processor host.{' '}
            <Link href="/docs/reference/limits">Limits and opt-in boundaries {'->'}</Link>
          </p>
        </main>
        <SiteFooter />
      </div>
    </>
  );
}
