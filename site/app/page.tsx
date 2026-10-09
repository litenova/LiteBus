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
import { defaultVersion, docsVersions } from '@/lib/versions';
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

const INSTALL_COMMAND = 'dotnet add package LiteBus.Extensions.Microsoft.DependencyInjection';

const REGISTER_CODE = `builder.Services.AddLiteBus(bus =>
{
    bus.AddMessaging(_ => { });
    bus.AddCommands(commands =>
        commands.RegisterFromAssembly(typeof(Program).Assembly));
});`;

const HANDLER_CODE = `public sealed record CreateProduct(string Name) : ICommand<Guid>;

public sealed class CreateProductHandler : ICommandHandler<CreateProduct, Guid>
{
    public Task<Guid> HandleAsync(
        CreateProduct command,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Guid.NewGuid());
    }
}`;

const SEND_CODE = `var id = await mediator.SendAsync(new CreateProduct("Widget"), cancellationToken);`;

/** One feature card: a title, one sentence, and the documentation page it links to. */
type Feature = Readonly<{ title: string; text: string; href: string; link: string }>;

const FEATURES: readonly Feature[] = [
  {
    title: 'Commands and Queries',
    text: 'Separate mediators for writes and reads, so a handler states which one it is.',
    href: '/docs/concepts/commands',
    link: 'Commands and queries',
  },
  {
    title: 'Events',
    text: 'Publish a plain .NET type to any number of handlers, with no shared base class.',
    href: '/docs/concepts/events',
    link: 'Events',
  },
  {
    title: 'Handler Pipeline',
    text: 'Guards, validators, pre-, post-, error and completion handlers run in a fixed order.',
    href: '/docs/concepts/handler-pipeline',
    link: 'The pipeline',
  },
  {
    title: 'Durable Inbox',
    text: 'Accept a command into storage first, then run it under a lease with retries.',
    href: '/docs/reliable-messaging/inbox',
    link: 'The inbox',
  },
  {
    title: 'Transactional Outbox',
    text: 'Store an event in the same transaction as your data and publish it after commit.',
    href: '/docs/reliable-messaging/outbox',
    link: 'The outbox',
  },
  {
    title: 'Brokers and Stores',
    text: 'RabbitMQ, Kafka, AWS SQS, Azure Service Bus, PostgreSQL and EF Core, each its own package.',
    href: '/docs/architecture/dependency-graph',
    link: 'Packages',
  },
  {
    title: 'Sagas',
    text: 'Keep correlated state across the commands of a long-running workflow.',
    href: '/docs/reliable-messaging/saga',
    link: 'Sagas',
  },
  {
    title: 'Compile-Time Checks',
    text: '20 analyzer rules catch missing handlers, duplicate handlers and contract gaps.',
    href: '/docs/reference/analyzers',
    link: 'Analyzers',
  },
];

/**
 * Landing page for the LiteBus site: the hero, three steps from install to a sent command, and the
 * feature cards that link into the documentation.
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
            <a className="release" href={changelogUrl}>
              <b>v{prerelease.release}</b>See what changed
            </a>
            <h1>Commands, Queries and Events for .NET</h1>
            <ul className="tags" aria-label="Platform and license">
              <li>.NET 10</li>
              <li>C#</li>
              <li>MIT</li>
              <li>Open Source</li>
              <li>Free for Commercial Use</li>
            </ul>
            <p>
              LiteBus sends commands, runs queries and publishes events through a handler pipeline in your
              process. When a message has to survive a restart, add the durable inbox and outbox.
            </p>
            <div className="install">
              <div className="cmd">
                <span>
                  <span className="dim">$ </span>
                  {INSTALL_COMMAND}
                </span>
                <CopyButton text={INSTALL_COMMAND} />
              </div>
              <p className="label">
                <Link href="/docs/getting-started">Get started</Link> sends your first command in a few minutes.
              </p>
            </div>
          </section>

          <section className="section" aria-labelledby="up-and-running-heading">
            <div className="section-head">
              <p className="kicker">Getting Started</p>
              <h2 id="up-and-running-heading">Up and Running in Three Steps</h2>
              <p>Register the modules, write a handler, send a message.</p>
            </div>
            <ol className="steps">
              <li>
                <b>Register LiteBus</b>
                <span>Scan your assembly for handlers when the host starts.</span>
                <pre data-label="Program.cs">
                  <code>{REGISTER_CODE}</code>
                </pre>
              </li>
              <li>
                <b>Write a handler</b>
                <span>A command is a type; its handler is a class LiteBus finds on its own.</span>
                <pre data-label="CreateProduct.cs">
                  <code>{HANDLER_CODE}</code>
                </pre>
              </li>
              <li>
                <b>Send the command</b>
                <span>Inject <code>ICommandMediator</code> wherever you need it.</span>
                <pre data-label="C#">
                  <code>{SEND_CODE}</code>
                </pre>
              </li>
            </ol>
            <p className="proof">
              <Link href="/docs/getting-started">Getting Started {'->'}</Link> covers queries, events and the
              durable modules.
            </p>
          </section>

          <section className="section" aria-labelledby="features-heading">
            <div className="section-head">
              <h2 id="features-heading">Features</h2>
              <p>Mediation in one package. Everything else is an opt-in package you add when you need it.</p>
            </div>
            <ul className="features">
              {FEATURES.map((feature) => (
                <li key={feature.title}>
                  <Link href={feature.href}>
                    <b>{feature.title}</b>
                    <span>{feature.text}</span>
                    <i>
                      {feature.link} {'->'}
                    </i>
                  </Link>
                </li>
              ))}
            </ul>
          </section>
        </main>
        <SiteFooter />
      </div>
    </>
  );
}
