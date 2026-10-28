using LiteBus.Events;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Integration tests proving two outbox processors with different lease owners publish every row exactly once.
/// </summary>
public sealed class PostgreSqlOutboxTwoHostLeasingTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const int MessageCount = 20;

    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlOutboxTwoHostLeasingTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public PostgreSqlOutboxTwoHostLeasingTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies two hosts draining one outbox table publish every message exactly once with no double delivery.
    /// </summary>
    /// <returns>A task that completes when both hosts drain the table and every row is verified.</returns>
    [SkippableFact]
    public async Task ProcessPendingAsync_TwoHostsOverOneTable_ShouldPublishEveryMessageExactlyOnce()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var recorderA = new EventRecorder();
        var recorderB = new EventRecorder();
        var providerA = BuildProvider(options, recorderA, "two-host-a");
        await using (providerA.ConfigureAwait(false))
        {
            var providerB = BuildProvider(options, recorderB, "two-host-b");
            await using (providerB.ConfigureAwait(false))
            {
                var outbox = providerA.GetRequiredService<IOutbox>();
                var messages = new List<(Guid MessageId, Guid OrderId)>(MessageCount);

                for (var index = 0; index < MessageCount; index++)
                {
                    var messageId = Guid.NewGuid();
                    var orderId = Guid.NewGuid();

                    await outbox.EnqueueAsync(OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.WithIdentity(
                        new OrderSubmittedIntegrationEvent { OrderId = orderId },
                        messageId)).ConfigureAwait(false);

                    messages.Add((messageId, orderId));
                }

                var processorA = providerA.GetRequiredService<IOutboxProcessor>();
                var processorB = providerB.GetRequiredService<IOutboxProcessor>();

                await Task.WhenAll(
                    RunUntilIdleAsync(processorA),
                    RunUntilIdleAsync(processorB)).ConfigureAwait(false);

                foreach (var (messageId, _) in messages)
                {
                    var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, options, messageId).ConfigureAwait(false);
                    row.Should().NotBeNull();
                    row!.Status.Should().Be(OutboxStatus.Published);
                    row.AttemptCount.Should().Be(1);
                }

                var delivered = recorderA.Events.Concat(recorderB.Events).ToList();
                delivered.Should().HaveCount(MessageCount);

                foreach (var (_, orderId) in messages)
                {
                    delivered.Count(@event => @event.OrderId == orderId).Should().Be(1);
                }
            }
        }
    }

    /// <summary>
    ///     Runs processing passes until one pass leases nothing.
    /// </summary>
    /// <param name="processor">The processor to drain.</param>
    /// <returns>A task that completes when the processor observes an idle pass.</returns>
    private static async Task RunUntilIdleAsync(IOutboxProcessor processor)
    {
        for (var pass = 0; pass < 50; pass++)
        {
            var result = await processor.ProcessPendingAsync().ConfigureAwait(false);

            if (result.LeasedCount == 0)
            {
                return;
            }
        }

        throw new InvalidOperationException("Processor did not drain the queue within the pass limit.");
    }

    /// <summary>
    ///     Builds one host provider over the shared outbox table with the given lease owner identity.
    /// </summary>
    /// <param name="options">The outbox store options that identify the shared test table.</param>
    /// <param name="recorder">The recorder that captures events dispatched by this host.</param>
    /// <param name="leaseOwner">The lease owner identity for this host.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(
        PostgreSqlOutboxStoreOptions options,
        EventRecorder recorder,
        string leaseOwner)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder =>
            {
                builder.Register<OrderSubmittedEventHandler>();
            });

            registry.AddOutbox(builder =>
            {
                builder.UsePostgreSqlStorage(postgres =>
                {
                    postgres.UseDataSource(_fixture.DataSource);
                    postgres.UseOptions(options);
                });

                builder.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted");

                builder.UseProcessorOptions(new OutboxProcessorOptions
                {
                    BatchSize = 4,
                    LeaseOwner = leaseOwner,
                    Retry = new RetryOptions { UseJitter = false }
                });

                builder.UseInProcessDispatch();
            });
        });

        return services.BuildServiceProvider();
    }
}
