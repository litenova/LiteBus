import './home.css';
import Link from 'next/link';
import type { ReactNode } from 'react';
import { CopyButton } from '@/components/copy-button';
import { HomeStepper } from '@/components/home-stepper';
import type { HomeStepperFile } from '@/components/home-stepper';
import { GitHubMark } from '@/components/logo';
import { SiteFooter } from '@/components/site-footer';
import { SiteHeader } from '@/components/site-header';
import { StatusBar } from '@/components/status-bar';
import { highlightToHtml } from '@/lib/highlight';
import {
  changelogUrl,
  companyName,
  companyUrl,
  latestRelease,
  nugetUrl,
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
      offers: { '@type': 'Offer', price: '0', priceCurrency: 'USD' },
      author: { '@id': `${siteUrl}/#organization` },
    },
  ],
};

const INSTALL_COMMAND = 'dotnet add package LiteBus.Extensions.Microsoft.DependencyInjection';

/** `Program.cs`: registers LiteBus and scans the entry assembly for handlers. */
const REGISTER_SOURCE = `builder.Services.AddLiteBus(bus =>
{
    bus.AddMessaging(_ => { });
    bus.AddCommands(commands =>
        commands.RegisterFromAssembly(typeof(Program).Assembly));
});`;

/** `CreateProduct.cs`: a command record and the handler LiteBus discovers. */
const HANDLER_SOURCE = `public sealed record CreateProduct(string Name) : ICommand<Guid>;

public sealed class CreateProductHandler : ICommandHandler<CreateProduct, Guid>
{
    public Task<Guid> HandleAsync(
        CreateProduct command,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Guid.NewGuid());
    }
}`;

/** `ProductsController.cs`: an illustrative controller that sends the command. */
const CONTROLLER_SOURCE = `[ApiController]
[Route("products")]
public sealed class ProductsController : ControllerBase
{
    private readonly ICommandMediator _mediator;

    public ProductsController(ICommandMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateProduct command,
        CancellationToken cancellationToken)
    {
        var id = await _mediator.SendAsync(command, cancellationToken);
        return Ok(id);
    }
}`;

/** One feature row: an API symbol, a title, one sentence, and the doc page it links to. */
type Feature = Readonly<{
  symbol: ReactNode;
  title: string;
  text: string;
  href: string;
  link: string;
}>;

const FEATURES: readonly Feature[] = [
  {
    symbol: <span className="tk-ifc">ICommandMediator</span>,
    title: 'Commands and Queries',
    text: 'Separate mediators for writes and reads, so a handler states which one it is.',
    href: '/docs/concepts/commands',
    link: 'Commands and queries',
  },
  {
    symbol: <span className="tk-ifc">IEventMediator</span>,
    title: 'Events',
    text: 'Publish a plain .NET type to any number of handlers, with no shared base class.',
    href: '/docs/concepts/events',
    link: 'Events',
  },
  {
    symbol: (
      <>
        <span className="tk-ifc">ICommandValidator</span>
        <span className="tk-pun">{'<'}</span>
        <span className="tk-cls">T</span>
        <span className="tk-pun">{'>'}</span>
      </>
    ),
    title: 'Handler Pipeline',
    text: 'Guards, validators, pre-, post-, error and completion handlers run in a fixed order.',
    href: '/docs/concepts/handler-pipeline',
    link: 'The pipeline',
  },
  {
    symbol: (
      <>
        <span className="tk-ifc">IInbox</span>
        <span className="tk-pun">.</span>
        <span className="tk-fn">AcceptAsync</span>
      </>
    ),
    title: 'Durable Inbox',
    text: 'Accept a command into storage first, then run it under a lease with retries.',
    href: '/docs/reliable-messaging/inbox',
    link: 'The inbox',
  },
  {
    symbol: (
      <>
        <span className="tk-ifc">IOutbox</span>
        <span className="tk-pun">.</span>
        <span className="tk-fn">EnqueueAsync</span>
      </>
    ),
    title: 'Transactional Outbox',
    text: 'Store an event in the same transaction as your data and publish it after commit.',
    href: '/docs/reliable-messaging/outbox',
    link: 'The outbox',
  },
  {
    symbol: <span className="tk-ifc">ITransportPublisher</span>,
    title: 'Brokers and Stores',
    text: 'RabbitMQ, Kafka, AWS SQS, Azure Service Bus, PostgreSQL and EF Core, each its own package.',
    href: '/docs/architecture/dependency-graph',
    link: 'Packages',
  },
  {
    symbol: <span className="tk-ifc">ISagaContext</span>,
    title: 'Sagas',
    text: 'Keep correlated state across the commands of a long-running workflow.',
    href: '/docs/reliable-messaging/saga',
    link: 'Sagas',
  },
  {
    symbol: (
      <span className="squig">
        <span className="tk-pun">LB1001</span>
      </span>
    ),
    title: 'Compile-Time Checks',
    text: 'Roslyn analyzers catch missing handlers, duplicate handlers and contract gaps at build time.',
    href: '/docs/reference/analyzers',
    link: 'Analyzers',
  },
  {
    symbol: (
      <>
        <span className="tk-ifc">ICommandMediator</span>
        <span className="tk-pun">.</span>
        <span className="tk-fn">TrySendAsync</span>
      </>
    ),
    title: 'Decisions as Values',
    text: 'Guards deny, validators reject and shortcuts answer; TrySendAsync returns the refusal instead of throwing.',
    href: '/docs/concepts/handler-pipeline',
    link: 'Refusals',
  },
  {
    symbol: (
      <>
        <span className="tk-pun">[</span>
        <span className="tk-cls">Audited</span>
        <span className="tk-pun">]</span>
      </>
    ),
    title: 'Auditing',
    text: 'Declare what a message audits; LiteBus writes one record per ending, denials and failures included.',
    href: '/docs/concepts/auditing',
    link: 'Auditing',
  },
  {
    symbol: (
      <>
        <span className="tk-pun">[</span>
        <span className="tk-cls">Authorized</span>
        <span className="tk-pun">]</span>
      </>
    ),
    title: 'Declared Metadata',
    text: 'Authorization, audit and your own declarations on the message, enforced at startup and by analyzers.',
    href: '/docs/concepts/message-definitions',
    link: 'Message definitions',
  },
  {
    symbol: <span className="tk-cls">Idempotency-Key</span>,
    title: 'Idempotency',
    text: 'Keyed commands run once and replay their result, with PostgreSQL and Marten stores and an ASP.NET Core filter.',
    href: '/docs/concepts/idempotency',
    link: 'Idempotency',
  },
  {
    symbol: <span className="tk-ifc">IUnitOfWork</span>,
    title: 'Unit of Work and Domain Events',
    text: 'Commit at a fixed point after the handler, with collected domain events staged into the outbox first.',
    href: '/docs/reliable-messaging/domain-events-and-unit-of-work',
    link: 'Unit of work',
  },
  {
    symbol: <span className="tk-fn">AddLiteBusManagementEndpoints</span>,
    title: 'Operations',
    text: 'Requeue, purge and drain over HTTP, health checks per probe, and OpenTelemetry traces and metrics.',
    href: '/docs/operations',
    link: 'Operations',
  },
];

/**
 * Landing page for the LiteBus site: the hero with the install command, the three-step editor,
 * and the feature rows that link into the documentation.
 *
 * A server component: it highlights the three example files with `highlightToHtml` and passes
 * the HTML to the client `HomeStepper`, so no highlighting runs in the browser.
 */
export default async function HomePage() {
  const [registerHtml, handlerHtml, controllerHtml] = await Promise.all([
    highlightToHtml(REGISTER_SOURCE, 'csharp'),
    highlightToHtml(HANDLER_SOURCE, 'csharp'),
    highlightToHtml(CONTROLLER_SOURCE, 'csharp'),
  ]);

  const files: readonly HomeStepperFile[] = [
    {
      name: 'Program.cs',
      crumbs: ['src', 'Program.cs'],
      html: registerHtml,
      title: 'Register LiteBus',
      text: 'Scan your assembly for handlers when the host starts.',
    },
    {
      name: 'CreateProduct.cs',
      crumbs: ['src', 'CreateProduct.cs', 'CreateProductHandler'],
      html: handlerHtml,
      title: 'Write a handler',
      text: 'A command is a type; its handler is a class LiteBus finds on its own.',
    },
    {
      name: 'ProductsController.cs',
      crumbs: ['src', 'Controllers', 'ProductsController.cs', 'Create'],
      html: controllerHtml,
      title: 'Send the command',
      text: (
        <>
          Inject <code>ICommandMediator</code> wherever you need it.
        </>
      ),
    },
  ];

  return (
    <>
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: JSON.stringify(structuredData) }}
      />
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      <div className="app">
        <SiteHeader current="home" />
        <main id="main" className="grow" tabIndex={-1}>
          <section className="hero">
            <div className="in">
              <a className="chip" href={changelogUrl}>
                <span className="pulse" aria-hidden="true" />
                <b>v{latestRelease}</b>
                <i>See what changed {'->'}</i>
              </a>
              <h1>Message Mediation and Durable Messaging for .NET</h1>
              <p className="lead">
                Commands, queries and events through one handler pipeline, with an opt-in durable inbox, outbox
                and sagas.
              </p>
              <div className="actions">
                <Link className="btn" href="/docs/getting-started">
                  Get Started
                </Link>
                <div className="install">
                  <span className="pr" aria-hidden="true">
                    {'>'}
                  </span>
                  <span className="cmdbox">
                    <span className="ghost" aria-hidden="true">
                      {INSTALL_COMMAND}
                    </span>
                    <span className="typed">{INSTALL_COMMAND}</span>
                  </span>
                  <CopyButton text={INSTALL_COMMAND} />
                </div>
              </div>
              <dl className="facts">
                <div>
                  <dt>Target</dt>
                  <dd>.NET 10</dd>
                </div>
                <div>
                  <dt>Language</dt>
                  <dd>C#</dd>
                </div>
                <div>
                  <dt>License</dt>
                  <dd>MIT</dd>
                </div>
              </dl>
            </div>
          </section>

          <section className="sec" aria-labelledby="up-and-running-heading">
            <div className="in">
              <p className="kicker">{'// Getting Started'}</p>
              <h2 id="up-and-running-heading">Up and Running in Three Steps</h2>
              <p className="sub">Register the modules, write a handler, send a message.</p>
              <HomeStepper files={files} />
              <Link className="next" href="/docs/getting-started">
                <span className="pr" aria-hidden="true">
                  {'>'}
                </span>
                <span>
                  <b>Getting Started</b>
                  <span className="d">Covers queries, events and the durable modules.</span>
                </span>
                <span className="go">
                  <kbd>Enter</kbd>
                  <span className="arrow" aria-hidden="true">
                    {'->'}
                  </span>
                </span>
              </Link>
            </div>
          </section>

          <section className="sec" aria-labelledby="features-heading">
            <div className="in">
              <p className="kicker">{'// Features'}</p>
              <h2 id="features-heading">Features</h2>
              <p className="sub">
                Mediation in one package. Everything else is an opt-in package you add when you need it.
              </p>
              <div className="feats">
                {FEATURES.map((feature) => (
                  <Link key={feature.title} className="feat" href={feature.href}>
                    <span className="sym">{feature.symbol}</span>
                    <b>{feature.title}</b>
                    <span className="d">{feature.text}</span>
                    <span className="go">
                      {feature.link} {'->'}
                    </span>
                  </Link>
                ))}
              </div>
            </div>
          </section>

          <section className="sec mediatr" aria-labelledby="mediatr-heading">
            <div className="in">
              <p className="kicker">{'// MediatR Comparison'}</p>
              <h2 id="mediatr-heading">An MIT-Licensed Alternative to MediatR</h2>
              <p className="sub">
                MediatR 13.0.0 and later ships under a commercial license. LiteBus is MIT licensed, with separate
                contracts for commands, queries and events and the durable modules built in.
              </p>
              <Link className="next" href="/docs/migration/mediatr-differences">
                <span className="pr" aria-hidden="true">
                  {'>'}
                </span>
                <span>
                  <b>Compare LiteBus and MediatR</b>
                  <span className="d">The message model, the pipeline, and moving an existing application.</span>
                </span>
                <span className="go">
                  <kbd>Enter</kbd>
                  <span className="arrow" aria-hidden="true">
                    {'->'}
                  </span>
                </span>
              </Link>
            </div>
          </section>
        </main>
        <SiteFooter />
        <StatusBar
          end={
            <>
              <span className="hide-sm">{companyName}</span>
              <a className="gh" href={repositoryUrl} aria-label="LiteBus on GitHub">
                <GitHubMark />
                GitHub
              </a>
              <a className="hide-sm" href={nugetUrl}>
                NuGet
              </a>
            </>
          }
        >
          <a href={changelogUrl}>v{latestRelease}</a>
          <span className="hide-sm">.NET 10</span>
          <span className="hide-sm">MIT</span>
        </StatusBar>
      </div>
    </>
  );
}
