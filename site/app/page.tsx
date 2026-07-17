import Link from 'next/link';

export default function HomePage() {
  return (
    <main className="litebus-hero">
      <img src="/logo.svg" alt="LiteBus" />
      <div>
        <h1>Clean message pipelines for .NET.</h1>
        <p>
          Commands, queries, events, inbox, and outbox processing with explicit contracts,
          independent adapters, and opt-in dependencies.
        </p>
      </div>
      <div className="litebus-actions">
        <Link href="/docs">Read the documentation</Link>
        <Link className="secondary" href="https://github.com/litenova/LiteBus">View on GitHub</Link>
      </div>
    </main>
  );
}
