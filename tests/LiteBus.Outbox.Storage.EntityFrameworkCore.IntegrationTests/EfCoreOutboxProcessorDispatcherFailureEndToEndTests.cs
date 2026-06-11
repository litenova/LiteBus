using LiteBus.Outbox.Abstractions;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests;

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
        await EfCoreOutboxE2eSupport.EnsureOutboxTableAsync(_fixture.ConnectionString, storeOptions);

        await using var provider = EfCoreOutboxE2eSupport.BuildProvider<DispatcherFailureOutboxDbContext>(
            _fixture.ConnectionString,
            storeOptions,
            new OutboxE2eComposition
            {
                Clock = clock,
                UseFailingDispatcher = true,
                MaxAttempts = 5,
                InitialDelay = TimeSpan.FromMinutes(2),
                LeaseOwner = "efcore-outbox-dispatcher-failure"
            });

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxEnqueueItems.WithIdentity(
            new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
            messageId));

        await processor.ProcessPendingAsync();

        var row = await EfCoreOutboxTableReaders.ReadOutboxAsync(_fixture.ConnectionString, storeOptions, messageId);
        row!.Status.Should().Be(OutboxStatus.Failed);
        row.VisibleAfter.Should().Be(EfCoreOutboxE2eSupport.BaseTime.AddMinutes(2));
    }

    private sealed class DispatcherFailureOutboxDbContext : EfCoreOutboxE2eDbContext
    {
        public DispatcherFailureOutboxDbContext(
            DbContextOptions<DispatcherFailureOutboxDbContext> options,
            EfCoreOutboxStoreOptions storeOptions)
            : base(options, storeOptions)
        {
        }
    }
}