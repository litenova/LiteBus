using LiteBus.Outbox.Abstractions;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class EfCoreOutboxProcessorDeadLetterEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const string TableName = "outbox_ef_dead_letter";

    private readonly PostgreSqlFixture _fixture;

    public EfCoreOutboxProcessorDeadLetterEndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenMaxAttemptsExceeded_ShouldMoveToDeadLetter()
    {
        var storeOptions = EfCoreOutboxE2eSupport.CreateStoreOptions(TableName);
        await EfCoreOutboxE2eSupport.EnsureOutboxTableAsync(_fixture.ConnectionString, storeOptions);

        await using var provider = EfCoreOutboxE2eSupport.BuildProvider<DeadLetterOutboxDbContext>(
            _fixture.ConnectionString,
            storeOptions,
            new OutboxE2eComposition
            {
                UseFailingDispatcher = true,
                MaxAttempts = 1,
                LeaseOwner = "efcore-outbox-dead-letter"
            });

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.WithIdentity(
            new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
            messageId));

        await processor.ProcessPendingAsync();

        var row = await EfCoreOutboxTableReaders.ReadOutboxAsync(_fixture.ConnectionString, storeOptions, messageId);
        row!.Status.Should().Be(OutboxStatus.DeadLettered);
    }

    private sealed class DeadLetterOutboxDbContext : EfCoreOutboxE2eDbContext
    {
        public DeadLetterOutboxDbContext(DbContextOptions<DeadLetterOutboxDbContext> options, EntityFrameworkCoreOutboxStoreOptions storeOptions)
            : base(options, storeOptions)
        {
        }
    }
}