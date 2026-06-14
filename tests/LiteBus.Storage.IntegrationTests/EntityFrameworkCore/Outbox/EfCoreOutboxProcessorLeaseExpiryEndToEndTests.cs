using LiteBus.Outbox.Abstractions;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LiteBus.Outbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

[Collection(PostgreSqlCollection.Name)]
public sealed class EfCoreOutboxProcessorLeaseExpiryEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const string TableName = "outbox_ef_lease_expiry";

    private readonly PostgreSqlFixture _fixture;

    public EfCoreOutboxProcessorLeaseExpiryEndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenLeaseExpires_ShouldReclaimAndPublishMessage()
    {
        var clock = new ManualTimeProvider(EfCoreOutboxE2eSupport.BaseTime);
        var storeOptions = EfCoreOutboxE2eSupport.CreateStoreOptions(TableName);
        var recorder = new EventRecorder();

        await EfCoreOutboxE2eSupport.EnsureOutboxTableAsync(_fixture.ConnectionString, storeOptions).ConfigureAwait(false);

         var provider = EfCoreOutboxE2eSupport.BuildProvider<LeaseExpiryOutboxDbContext>(             _fixture.ConnectionString,             storeOptions,             new OutboxE2eComposition             {                 Recorder = recorder,                 Clock = clock,                 LeaseOwner = "efcore-outbox-lease-expiry"             });
         await using (provider.ConfigureAwait(true))
         {

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var leaseStore = provider.GetRequiredService<IOutboxLeaseStore>();
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.WithIdentity(
            new OrderSubmittedIntegrationEvent { OrderId = orderId },
            messageId)).ConfigureAwait(false);

        await leaseStore.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "stale-publisher",
            Now = EfCoreOutboxE2eSupport.BaseTime,
            LeaseDuration = TimeSpan.FromSeconds(20)
        }).ConfigureAwait(false);

        clock.Advance(TimeSpan.FromMinutes(1));
        await processor.ProcessPendingAsync().ConfigureAwait(false);

        recorder.Events.Should().ContainSingle(@event => @event.OrderId == orderId);

        var row = await EfCoreOutboxTableReaders.ReadOutboxAsync(_fixture.ConnectionString, storeOptions, messageId).ConfigureAwait(false);
        row!.Status.Should().Be(OutboxStatus.Published);
        row.AttemptCount.Should().Be(2);
        }
    }

    private sealed class LeaseExpiryOutboxDbContext : EfCoreOutboxE2eDbContext
    {
        public LeaseExpiryOutboxDbContext(DbContextOptions<LeaseExpiryOutboxDbContext> options, EntityFrameworkCoreOutboxStoreOptions storeOptions)
            : base(options, storeOptions)
        {
        }
    }
}
