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
///     Drives the EF Core outbox store through every state its processor uses.
/// </summary>
internal static class OutboxSmoke
{
    /// <summary>
    ///     Runs the outbox scenario.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>A task that completes when the scenario has passed.</returns>
    internal static async Task RunAsync(string connectionString)
    {
        var store = new EfCoreOutboxStore(
            _ => Task.FromResult<IOutboxDbContext>(SmokeDbContext.Create(connectionString)),
            new EntityFrameworkCoreOutboxStoreOptions());

        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();
        var envelope = new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "smoke.events.submitted",
            ContractVersion = 1,
            Payload = "{\"note\":\"Grüße 漢字 😀\"}",
            CreatedAt = now,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            IdempotencyKey = $"smoke:{messageId:N}"
        };

        var first = await store.EnqueueAsync(envelope).ConfigureAwait(false);
        Smoke.Expect(first.Outcome == OutboxEnqueueOutcome.Enqueued, "the first enqueue is stored");

        // A second insert of the same key raises the provider's unique violation, which the store must recognize.
        var duplicate = await store.EnqueueAsync(envelope with { Id = Guid.NewGuid() }).ConfigureAwait(false);
        Smoke.Expect(duplicate.Outcome == OutboxEnqueueOutcome.AlreadyEnqueued, "a duplicate key reports AlreadyEnqueued");

        var leased = await LeaseAsync(store, "smoke-publisher-1").ConfigureAwait(false);

        var renewed = await store.RenewLeaseAsync(new LeaseRenewalRequest(
            messageId,
            "smoke-publisher-1",
            leased.LeaseGeneration,
            TimeSpan.FromMinutes(1),
            DateTimeOffset.UtcNow.AddMinutes(1))).ConfigureAwait(false);
        Smoke.Expect(renewed, "the lease owner renews its lease");

        await store.PersistAsync([leased.AsFailed(MessageFailure.FromError("broker down", DateTimeOffset.UtcNow))]).ConfigureAwait(false);
        var retried = await LeaseAsync(store, "smoke-publisher-2").ConfigureAwait(false);

        await store.PersistAsync([retried.AsDeadLettered(MessageFailure.FromError("poison", DateTimeOffset.UtcNow))]).ConfigureAwait(false);
        var requeued = await store.RequeueAsync([messageId]).ConfigureAwait(false);
        Smoke.Expect(requeued.Requeued == 1, "a dead-lettered message requeues");

        var final = await LeaseAsync(store, "smoke-publisher-3").ConfigureAwait(false);
        await store.PersistAsync([final.AsPublished(DateTimeOffset.UtcNow)]).ConfigureAwait(false);

        var page = await store.QueryAsync(
            new OutboxMessageFilter { MessageIds = [messageId] },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);
        Smoke.Expect(page.Items.Count == 1 && page.Items[0].Status == OutboxStatus.Published, "the message ends published");
        Smoke.Expect(page.Items[0].Payload == envelope.Payload, "the payload round-trips unchanged");

        var counts = await store.GetStatusCountsAsync().ConfigureAwait(false);
        Smoke.Expect(counts.GetValueOrDefault(OutboxStatus.Published) == 1, "status counts see the published message");

        var deleted = await store.DeletePublishedOlderThanAsync(DateTimeOffset.UtcNow.AddMinutes(1)).ConfigureAwait(false);
        Smoke.Expect(deleted == 1, "retention deletes the published message");

        var purged = await store.PurgeAsync(new OutboxMessageFilter { MessageIds = [messageId] }).ConfigureAwait(false);
        Smoke.Expect(purged == 0, "nothing is left to purge");
    }

    /// <summary>
    ///     Leases the single pending message.
    /// </summary>
    /// <param name="store">The outbox store.</param>
    /// <param name="owner">The lease owner.</param>
    /// <returns>The leased envelope.</returns>
    private static async Task<OutboxEnvelope> LeaseAsync(EfCoreOutboxStore store, string owner)
    {
        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 10,
            LeaseOwner = owner,
            Now = DateTimeOffset.UtcNow.AddSeconds(5),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        Smoke.Expect(leased.Count == 1, $"{owner} leases exactly one message");
        return leased[0];
    }
}
