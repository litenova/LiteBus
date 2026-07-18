using LiteBus.Outbox.Abstractions;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LiteBus.Outbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

[Collection(PostgreSqlCollection.Name)]
public sealed class EfCoreOutboxProcessorDispatcherFailureEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const string TableName = "outbox_ef_dispatcher_failure";

    private readonly PostgreSqlFixture _fixture;

    public EfCoreOutboxProcessorDispatcherFailureEndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenDispatcherFails_ShouldMarkFailedWithVisibleAfter()
    {
        var clock = new ManualTimeProvider(EfCoreOutboxE2eSupport.BaseTime);
        var storeOptions = EfCoreOutboxE2eSupport.CreateStoreOptions(TableName);
        await EfCoreOutboxE2eSupport.EnsureOutboxTableAsync(_fixture.ConnectionString, storeOptions).ConfigureAwait(false);

         var provider = EfCoreOutboxE2eSupport.BuildProvider<DispatcherFailureOutboxDbContext>(             _fixture.ConnectionString,             storeOptions,             new OutboxE2eComposition             {                 Clock = clock,                 UseFailingDispatcher = true,                 MaxAttempts = 5,                 InitialDelay = TimeSpan.FromMinutes(2),                 LeaseOwner = "efcore-outbox-dispatcher-failure"             });
         await using (provider.ConfigureAwait(true))
         {

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.WithIdentity(
            new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
            messageId)).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var row = await EfCoreOutboxTableReaders.ReadOutboxAsync(_fixture.ConnectionString, storeOptions, messageId).ConfigureAwait(false);
        row!.Status.Should().Be(OutboxStatus.Failed);
        row.VisibleAfter.Should().Be(EfCoreOutboxE2eSupport.BaseTime.AddMinutes(2));
        }
    }

    private sealed class DispatcherFailureOutboxDbContext : EfCoreOutboxE2eDbContext
    {
        public DispatcherFailureOutboxDbContext(
            DbContextOptions<DispatcherFailureOutboxDbContext> options,
            EntityFrameworkCoreOutboxStoreOptions storeOptions)
            : base(options, storeOptions)
        {
        }
    }
}
