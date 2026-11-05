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
///     Drives the EF Core inbox store through acceptance, leasing, retry, and completion.
/// </summary>
internal static class InboxSmoke
{
    /// <summary>
    ///     Runs the inbox scenario.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>A task that completes when the scenario has passed.</returns>
    internal static async Task RunAsync(string connectionString)
    {
        var store = new EfCoreInboxStore(
            _ => Task.FromResult<IInboxDbContext>(SmokeDbContext.Create(connectionString)),
            new EntityFrameworkCoreInboxStoreOptions());

        var commandId = Guid.NewGuid();
        var envelope = new InboxEnvelope
        {
            Id = commandId,
            ContractName = "smoke.commands.ship",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            IdempotencyKey = $"ship:{commandId:N}"
        };

        var first = await store.EnqueueAsync(envelope).ConfigureAwait(false);
        Smoke.Expect(first.Outcome == InboxAcceptOutcome.Accepted, "the first accept is stored");

        var duplicate = await store.EnqueueAsync(envelope with { Id = Guid.NewGuid() }).ConfigureAwait(false);
        Smoke.Expect(duplicate.Outcome == InboxAcceptOutcome.AlreadyAccepted, "a duplicate key reports AlreadyAccepted");

        var leased = await LeaseAsync(store, "smoke-worker-1").ConfigureAwait(false);
        var renewed = await store.RenewLeaseAsync(new LeaseRenewalRequest(
            commandId,
            "smoke-worker-1",
            leased.LeaseGeneration,
            TimeSpan.FromMinutes(1),
            DateTimeOffset.UtcNow.AddMinutes(1))).ConfigureAwait(false);
        Smoke.Expect(renewed, "the lease owner renews its lease");

        await store.PersistAsync([leased.AsFailed(MessageFailure.FromError("handler failed", DateTimeOffset.UtcNow))]).ConfigureAwait(false);
        var retried = await LeaseAsync(store, "smoke-worker-2").ConfigureAwait(false);
        await store.PersistAsync([retried.AsCompleted()]).ConfigureAwait(false);

        var page = await store.QueryAsync(
            new InboxMessageFilter { MessageIds = [commandId] },
            new InboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);
        Smoke.Expect(page.Items.Count == 1 && page.Items[0].Status == InboxStatus.Completed, "the command ends completed");

        var deleted = await store.DeleteCompletedOlderThanAsync(DateTimeOffset.UtcNow.AddMinutes(1)).ConfigureAwait(false);
        Smoke.Expect(deleted == 1, "retention deletes the completed command");
    }

    /// <summary>
    ///     Leases the single pending command.
    /// </summary>
    /// <param name="store">The inbox store.</param>
    /// <param name="owner">The lease owner.</param>
    /// <returns>The leased envelope.</returns>
    private static async Task<InboxEnvelope> LeaseAsync(EfCoreInboxStore store, string owner)
    {
        var leased = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 10,
            LeaseOwner = owner,
            Now = DateTimeOffset.UtcNow.AddSeconds(5),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        Smoke.Expect(leased.Count == 1, $"{owner} leases exactly one command");
        return leased[0];
    }
}
