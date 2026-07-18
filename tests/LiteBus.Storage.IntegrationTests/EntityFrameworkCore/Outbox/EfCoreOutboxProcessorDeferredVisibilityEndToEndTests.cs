using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LiteBus.Outbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

[Collection(PostgreSqlCollection.Name)]
public sealed class EfCoreOutboxProcessorDeferredVisibilityEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const string TableName = "outbox_ef_deferred_visibility";

    private readonly PostgreSqlFixture _fixture;

    public EfCoreOutboxProcessorDeferredVisibilityEndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAsync_WithVisibleAfter_ShouldDeferPublishingUntilDue()
    {
        var storeOptions = EfCoreOutboxE2eSupport.CreateStoreOptions(TableName);
        var recorder = new EventRecorder();
        var visibleAfter = DateTimeOffset.UtcNow.AddHours(1);

        await EfCoreOutboxE2eSupport.EnsureOutboxTableAsync(_fixture.ConnectionString, storeOptions).ConfigureAwait(false);

        var provider = EfCoreOutboxE2eSupport.BuildProvider<DeferredVisibilityOutboxDbContext>(
            _fixture.ConnectionString,
            storeOptions,
            new OutboxE2eComposition
            {
                Recorder = recorder,
                LeaseOwner = "efcore-outbox-deferred-visibility"
            });
        await using (provider.ConfigureAwait(false))
        {
            var outbox = provider.GetRequiredService<IOutbox>();
            var processor = provider.GetRequiredService<IOutboxProcessor>();
            var messageId = Guid.NewGuid();

            await outbox.EnqueueAsync(OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.From(
                new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
                OutboxEnqueueMetadata.Immediate with
                {
                    Identity = new MessageIdentity.Supplied(messageId),
                    Visibility = new MessageVisibility.At(visibleAfter)
                })).ConfigureAwait(false);

            await processor.ProcessPendingAsync().ConfigureAwait(false);
            recorder.Events.Should().BeEmpty();

            await PostgreSqlDatabaseTimeTestSupport.MakeVisibleAsync(
                _fixture.ConnectionString,
                storeOptions.SchemaName,
                storeOptions.TableName,
                messageId).ConfigureAwait(false);
            await processor.ProcessPendingAsync().ConfigureAwait(false);

            recorder.Events.Should().ContainSingle();
        }
    }

    private sealed class DeferredVisibilityOutboxDbContext : EfCoreOutboxE2eDbContext
    {
        public DeferredVisibilityOutboxDbContext(
            DbContextOptions<DeferredVisibilityOutboxDbContext> options,
            EntityFrameworkCoreOutboxStoreOptions storeOptions)
            : base(options, storeOptions)
        {
        }
    }
}
