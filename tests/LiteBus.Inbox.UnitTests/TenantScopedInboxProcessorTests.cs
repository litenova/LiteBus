using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Messaging.Abstractions.Processing;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies processor-level tenant leasing across complete inbox passes.
/// </summary>
public sealed class TenantScopedInboxProcessorTests
{
    /// <summary>
    ///     Verifies dedicated tenant processors lease and complete only their configured partitions.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_WithTenantFilters_ShouldIsolateProcessorPasses()
    {
        var store = new InMemoryInboxStore();
        var registry = new MessageContractRegistry();
        registry.Register<TenantCommand>("tenant.commands.process");
        var inbox = InboxWriterTestFactory.Create(
            store,
            registry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);
        var dispatcher = new RecordingTenantDispatcher();

        var tenantA = await inbox.AcceptAsync(InboxAcceptItem<TenantCommand>.From(
            new TenantCommand { Value = "a" },
            InboxAcceptMetadata.Immediate with
            {
                Tenant = new TenantScope.Isolated("tenant-a")
            })).ConfigureAwait(false);
        var tenantB = await inbox.AcceptAsync(InboxAcceptItem<TenantCommand>.From(
            new TenantCommand { Value = "b" },
            InboxAcceptMetadata.Immediate with
            {
                Tenant = new TenantScope.Isolated("tenant-b")
            })).ConfigureAwait(false);

        var tenantAProcessor = CreateProcessor(store, dispatcher, "tenant-a");
        var tenantBProcessor = CreateProcessor(store, dispatcher, "tenant-b");

        await tenantAProcessor.ProcessPendingAsync().ConfigureAwait(false);

        store.Get(tenantA.Id).Status.Should().Be(InboxStatus.Completed);
        store.Get(tenantB.Id).Status.Should().Be(InboxStatus.Pending);
        dispatcher.Tenants.Should().Equal("tenant-a");

        await tenantBProcessor.ProcessPendingAsync().ConfigureAwait(false);

        store.Get(tenantB.Id).Status.Should().Be(InboxStatus.Completed);
        dispatcher.Tenants.Should().Equal("tenant-a", "tenant-b");
    }

    private static PipelinedInboxProcessor CreateProcessor(
        InMemoryInboxStore store,
        IInboxDispatcher dispatcher,
        string tenantId)
    {
        return new PipelinedInboxProcessor(
            store,
            store,
            dispatcher,
            new InboxProcessorOptions
            {
                BatchSize = 10,
                DispatcherConcurrency = 1,
                LeaseOwner = $"worker-{tenantId}",
                TenantId = tenantId,
                Retry = new RetryOptions { UseJitter = false }
            },
            TimeProvider.System,
            []);
    }

    private sealed record TenantCommand
    {
        public string Value { get; init; } = string.Empty;
    }

    private sealed class RecordingTenantDispatcher : IInboxDispatcher
    {
        private readonly List<string> _tenants = [];

        public IReadOnlyList<string> Tenants => _tenants;

        public Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _tenants.Add(envelope.TenantId!);
            return Task.CompletedTask;
        }
    }
}
