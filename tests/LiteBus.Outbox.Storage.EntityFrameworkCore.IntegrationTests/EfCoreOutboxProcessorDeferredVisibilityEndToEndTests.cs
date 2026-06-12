using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests;

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
        var clock = new ManualTimeProvider(EfCoreOutboxE2eSupport.BaseTime);
        var storeOptions = EfCoreOutboxE2eSupport.CreateStoreOptions(TableName);
        var recorder = new EventRecorder();
        var visibleAfter = EfCoreOutboxE2eSupport.BaseTime.AddHours(1);

        await EfCoreOutboxE2eSupport.EnsureOutboxTableAsync(_fixture.ConnectionString, storeOptions);

        await using var provider = EfCoreOutboxE2eSupport.BuildProvider<DeferredVisibilityOutboxDbContext>(
            _fixture.ConnectionString,
            storeOptions,
            new OutboxE2eComposition
            {
                Recorder = recorder,
                Clock = clock,
                LeaseOwner = "efcore-outbox-deferred-visibility"
            });

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.From(
            new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
            OutboxEnqueueMetadata.Immediate with
            {
                Identity = new MessageIdentity.Supplied(messageId),
                Visibility = new MessageVisibility.At(visibleAfter)
            }));

        await processor.ProcessPendingAsync();
        recorder.Events.Should().BeEmpty();

        clock.Advance(TimeSpan.FromHours(1));
        await processor.ProcessPendingAsync();

        recorder.Events.Should().ContainSingle();
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