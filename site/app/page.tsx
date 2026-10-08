import { CopyButton } from '@/components/copy-button';
import { SiteFooter } from '@/components/site-footer';
import { SiteHeader } from '@/components/site-header';
import {
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

var id = await mediator.SendAsync(
    new CreateProduct("Widget", 9.99m),
    cancellationToken);`;

const HANDLER_CODE = `public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, ProductDto>
{
    private readonly IProductRepository _repository;

    public CreateProductCommandHandler(IProductRepository repository)
    {
        _repository = repository;
    }

    public async Task<ProductDto> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        var product = new Product(command.Name, command.Price);
        await _repository.SaveAsync(product, cancellationToken);
        return new ProductDto(product.Id, product.Name, product.Price);
    }
}`;

const INSTALL_COMMAND = 'dotnet add package LiteBus.Extensions.Microsoft.DependencyInjection';

/**
 * Landing page for the LiteBus site.
 *
 * Presents the release pill, the mediation sample, the module cards, the
 * package table, and the MediatR comparison in the shared Fuse-style frame.
 */
export default function HomePage() {
  const prerelease = docsVersions.find((version) => version.status === 'prerelease') ?? defaultVersion;

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
            <Link className="release" href={versionBasePath(prerelease)}>
              <b>{prerelease.label} preview</b>Read the pre-release documentation
            </Link>
            <h1>Command, Query, and Event Mediation for .NET</h1>
            <ul className="tags" aria-label="License and platform">
              <li>MIT licensed</li>
              <li>.NET 10</li>
              <li>Opt-in packages</li>
            </ul>
            <p>
              LiteBus mediates commands, queries, and events with handler pipelines, and adds
              optional durable inbox, outbox, saga, storage, and transport modules.
            </p>
            <div className="install">
              <p className="label">Install the mediation package:</p>
              <div className="cmd">
                <span>
                  <span className="dim">$ </span>
                  {INSTALL_COMMAND}
                </span>
                <CopyButton text={INSTALL_COMMAND} />
              </div>
              <p className="label">
                New to LiteBus? <Link href="/docs/getting-started">Get Started</Link> walks through your
                first command, query, and event.
              </p>
            </div>
          </section>

          <section className="section" aria-labelledby="register-and-send-heading">
            <p className="kicker">Basics</p>
            <h2 id="register-and-send-heading">Register and Send</h2>
            <p className="lede">Register handlers from an assembly and send a command through the mediator.</p>
            <pre data-label="Program.cs">
              <code>{QUICKSTART_CODE}</code>
            </pre>
            <pre data-label="CreateProductHandler.cs">
              <code>{HANDLER_CODE}</code>
            </pre>
          </section>

          <section className="section" aria-labelledby="what-litebus-includes-heading">
            <p className="kicker">Modules</p>
            <h2 id="what-litebus-includes-heading">What LiteBus Includes</h2>
            <p className="lede">Mediation, durable messaging, and adapters in opt-in packages.</p>
            <ul className="features">
              <li>
                <Link href="/docs/concepts/commands">
                  <b>Commands and Queries</b>
                  <span>Separate command and query mediators encode write and read intent for CQS applications.</span>
                  <i>Read the commands guide {'->'}</i>
                </Link>
              </li>
              <li>
                <Link href="/docs/concepts/events">
                  <b>Domain Events</b>
                  <span>Publish plain .NET event types to zero or more handlers without requiring a shared event base class.</span>
                  <i>Read the events guide {'->'}</i>
                </Link>
              </li>
              <li>
                <Link href="/docs/concepts/handler-pipeline">
                  <b>Handler Pipelines</b>
                  <span>Run validation and cross-cutting work through named pre-, post-, and error-handler stages.</span>
                  <i>Read the pipeline guide {'->'}</i>
                </Link>
              </li>
              <li>
                <Link href="/docs/reliable-messaging/inbox">
                  <b>Durable Inbox</b>
                  <span>Persist commands before deferred execution with leases, retries, receipts, and at-least-once delivery.</span>
                  <i>Read the inbox guide {'->'}</i>
                </Link>
              </li>
              <li>
                <Link href="/docs/reliable-messaging/outbox">
                  <b>Transactional Outbox</b>
                  <span>Store events with domain state through transactional writers, then publish them with an outbox processor.</span>
                  <i>Read the outbox guide {'->'}</i>
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
                <Link href="/docs/architecture/dependency-graph">
                  <b>Storage and Transport</b>
                  <span>Select PostgreSQL, EF Core, in-memory, RabbitMQ, Kafka, AWS SQS, or Azure Service Bus through opt-in adapters.</span>
                  <i>Read the package guide {'->'}</i>
                </Link>
              </li>
              <li>
                <Link href="/docs/architecture/hosted-services">
                  <b>Hosting and Operations</b>
                  <span>Run processors through host adapters and add OpenTelemetry, health checks, management endpoints, and analyzers.</span>
                  <i>Read the hosting guide {'->'}</i>
                </Link>
              </li>
            </ul>
          </section>

          <section className="section" aria-labelledby="install-only-what-you-run-heading">
            <p className="kicker">Packages</p>
            <h2 id="install-only-what-you-run-heading">Install Only What You Run</h2>
            <p className="lede">Reference only the adapters your host runs.</p>
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
              See <Link href="/docs/architecture/dependency-graph">Dependency Graph</Link> for every package and its role.
            </p>
          </section>

          <section className="section" aria-labelledby="moving-from-mediatr-heading">
            <p className="kicker">Migration</p>
            <h2 id="moving-from-mediatr-heading">Moving From MediatR</h2>
            <p>MediatR 13 moved to dual commercial and open-source licensing and requires a license key.</p>
            <p>LiteBus is MIT licensed, open source, free for commercial use, and will remain free.</p>
            <p>
              <Link href="/docs/migration/mediatr-differences">Compare LiteBus and MediatR {'->'}</Link>
            </p>
          </section>
        </main>
        <SiteFooter />
      </div>
    </>
  );
}
