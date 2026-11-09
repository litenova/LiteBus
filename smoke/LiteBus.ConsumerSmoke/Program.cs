using LiteBus.Commands.Storage.Marten;
using LiteBus.ConsumerSmoke;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
using Marten;
using Microsoft.EntityFrameworkCore;

// Exercises the LiteBus packages as an application does: from the packed packages, against the latest stable releases
// of their third-party dependencies. The mediator scenario sends generic messages to handlers registered by their open
// definitions and needs no database. Every storage call after it runs a code path that binds to a dependency API at
// runtime (ExecuteUpdate, ExecuteDelete, raw lease SQL, provider error codes, Marten sessions) on a real PostgreSQL
// server, so a dependency release that removes or changes one of those APIs fails here with the exception an
// application would see.
await Smoke.RunAsync("Generic message dispatch", GenericDispatchSmoke.RunAsync).ConfigureAwait(false);

var connectionString = Environment.GetEnvironmentVariable("LITEBUS_SMOKE_POSTGRES")
                       ?? throw new InvalidOperationException("Set LITEBUS_SMOKE_POSTGRES to a PostgreSQL connection string.");

Console.WriteLine($"EF Core {typeof(DbContext).Assembly.GetName().Version}, " +
                  $"Npgsql {typeof(Npgsql.NpgsqlConnection).Assembly.GetName().Version}, " +
                  $"Marten {typeof(DocumentStore).Assembly.GetName().Version}");

await using (var setup = SmokeDbContext.Create(connectionString))
{
    await setup.Database.EnsureDeletedAsync().ConfigureAwait(false);
    await setup.Database.EnsureCreatedAsync().ConfigureAwait(false);
}

await Smoke.RunAsync("EF Core outbox store", () => OutboxSmoke.RunAsync(connectionString)).ConfigureAwait(false);
await Smoke.RunAsync("EF Core inbox store", () => InboxSmoke.RunAsync(connectionString)).ConfigureAwait(false);
await Smoke.RunAsync("Marten idempotency store", () => MartenSmoke.RunAsync(connectionString)).ConfigureAwait(false);

Console.WriteLine("Consumer smoke test passed.");
