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

namespace LiteBus.ConsumerSmoke;

/// <summary>
///     Drives the Marten idempotency store, which runs Marten on whatever Npgsql the application resolves.
/// </summary>
internal static class MartenSmoke
{
    /// <summary>
    ///     Runs the Marten scenario.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>A task that completes when the scenario has passed.</returns>
    internal static async Task RunAsync(string connectionString)
    {
        var documentStore = DocumentStore.For(options =>
        {
            options.Connection(connectionString);
            options.DatabaseSchemaName = "litebus_smoke_marten";
        });

        await using (documentStore.ConfigureAwait(false))
        {
            var store = new MartenIdempotencyStore(documentStore);
            var key = $"smoke:{Guid.NewGuid():N}";

            var claim = await store.TryClaimAsync(key, "fp-1").ConfigureAwait(false);
            Smoke.Expect(claim.Outcome == IdempotencyClaimOutcome.Granted, "a new key is granted");

            await store.CompleteAsync(key, claim.ClaimToken!, "{\"receipt\":\"r-1\"}").ConfigureAwait(false);

            var replay = await store.TryClaimAsync(key, "fp-1").ConfigureAwait(false);
            Smoke.Expect(replay.Outcome == IdempotencyClaimOutcome.AlreadyCompleted, "a completed key replays");
            Smoke.Expect(replay.Payload == "{\"receipt\":\"r-1\"}", "the replay returns the stored payload");
        }
    }
}
