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

            Smoke.Expect(
                store is ISeparatelyCommittedIdempotencyStore,
                "the Marten store settles separately from the unit of work");

            var failedKey = $"smoke:{Guid.NewGuid():N}";

            var firstAttempt = await store.TryClaimAsync(failedKey, "fp-1").ConfigureAwait(false);
            Smoke.Expect(firstAttempt.Outcome == IdempotencyClaimOutcome.Granted, "a new key is granted before the commit");

            // The unit-of-work commit threw, so the claim is released instead of marked complete.
            await store.ReleaseAsync(failedKey, firstAttempt.ClaimToken!).ConfigureAwait(false);

            var retry = await store.TryClaimAsync(failedKey, "fp-1").ConfigureAwait(false);
            Smoke.Expect(retry.Outcome == IdempotencyClaimOutcome.Granted, "a retry after a failed commit runs again");

            await store.CompleteAsync(failedKey, retry.ClaimToken!, "{\"receipt\":\"r-2\"}").ConfigureAwait(false);

            var finalReplay = await store.TryClaimAsync(failedKey, "fp-1").ConfigureAwait(false);
            Smoke.Expect(finalReplay.Outcome == IdempotencyClaimOutcome.AlreadyCompleted, "the retried key replays once completed");
            Smoke.Expect(finalReplay.Payload == "{\"receipt\":\"r-2\"}", "the retried replay returns the stored payload");
        }
    }
}
