import Image from 'next/image';
import Link from 'next/link';

export default function HomePage() {
  return (
    <div className="litebus-home">
      <header className="litebus-home-header">
        <Link className="litebus-brand" href="/">
          <Image src="/icon.svg" alt="" width={32} height={32} />
          <span>LiteBus</span>
        </Link>
        <nav className="litebus-home-nav" aria-label="Main navigation">
          <Link href="/docs/getting-started">Get started</Link>
          <Link href="https://github.com/litenova/LiteBus">GitHub</Link>
        </nav>
      </header>

      <main>
        <section className="litebus-hero">
          <div>
            <div className="litebus-eyebrow">Messaging building blocks for .NET</div>
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
          </div>

          <div className="litebus-install" aria-label="Package installation commands">
            <div className="litebus-install-label">Start with the aggregate package</div>
            <pre>
              <code>{`dotnet add package LiteBus

services.AddLiteBus(bus =>
{
    bus.AddMediator();
});`}</code>
            </pre>
          </div>
        </section>

        <section className="litebus-features" aria-label="LiteBus capabilities">
          <article className="litebus-feature">
            <h2>Semantic mediators</h2>
            <p>Separate command, query, and event contracts with focused handler pipelines.</p>
          </article>
          <article className="litebus-feature">
            <h2>Reliable messaging</h2>
            <p>Use inbox, outbox, scheduling, retries, leases, and sagas as independent concerns.</p>
          </article>
          <article className="litebus-feature">
            <h2>Opt-in integrations</h2>
            <p>Add only the broker, storage, host, and observability adapters your process needs.</p>
          </article>
        </section>
      </main>
    </div>
  );
}
