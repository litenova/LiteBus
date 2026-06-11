using LiteBus.Outbox.Abstractions;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     End-to-end outbox processor tests with Entity Framework Core storage and PostgreSQL.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class EfCoreOutboxProcessorEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const string TableName = "outbox_ef_happy_path";

    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxProcessorEndToEndTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreOutboxProcessorEndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies that <see cref="IOutboxProcessor.ProcessPendingAsync" /> publishes a stored event through EF Core storage.
    /// </summary>
    /// <returns>A task that completes when the event is published.</returns>
    [Fact]
    public async Task ProcessPendingAsync_ShouldPublishEventThroughEfCoreStore()
    {
        var storeOptions = EfCoreOutboxE2eSupport.CreateStoreOptions(TableName);
        await EfCoreOutboxE2eSupport.EnsureOutboxTableAsync(_fixture.ConnectionString, storeOptions);

        var recorder = new EventRecorder();

        await using var provider = EfCoreOutboxE2eSupport.BuildProvider<HappyPathOutboxDbContext>(
            _fixture.ConnectionString,
            storeOptions,
            new OutboxE2eComposition
            {
                Recorder = recorder,
                LeaseOwner = "efcore-outbox-happy-path"
            });

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxEnqueueItems.WithIdentity(
            new OrderSubmittedIntegrationEvent { OrderId = orderId },
            messageId));

        await processor.ProcessPendingAsync();

        recorder.Events.Should().ContainSingle(@event => @event.OrderId == orderId);
    }

    private sealed class HappyPathOutboxDbContext : EfCoreOutboxE2eDbContext
    {
        public HappyPathOutboxDbContext(DbContextOptions<HappyPathOutboxDbContext> options, EfCoreOutboxStoreOptions storeOptions)
            : base(options, storeOptions)
        {
        }
    }
}