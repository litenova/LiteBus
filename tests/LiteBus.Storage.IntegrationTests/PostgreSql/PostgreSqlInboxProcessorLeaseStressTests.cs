using System.Collections.Concurrent;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Orchestration.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using IInboxProcessor = LiteBus.Inbox.Abstractions.IInboxProcessor;
using LiteBus.Storage.PostgreSql;
using LiteBus.Outbox;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Stress tests inbox lease semantics under parallel processor workers.
/// </summary>
public sealed class PostgreSqlInboxProcessorLeaseStressTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const int WorkerCount = 4;
    private const int MessageCount = 100;
    private const int MaxAttempts = 5;

    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlInboxProcessorLeaseStressTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL test fixture.</param>
    public PostgreSqlInboxProcessorLeaseStressTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies parallel workers complete every message once without duplicate terminal outcomes.
    /// </summary>
    /// <returns>A task that completes when the stress test finishes.</returns>
    [Fact]
    public async Task ProcessPendingAsync_parallel_workers_should_produce_single_terminal_state_per_message()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var tracker = new InvocationTracker();
         var provider = BuildProvider(_fixture, options, tracker);
         await using (provider.ConfigureAwait(false))
         {

        var scheduler = provider.GetRequiredService<IInbox>();
        var messageIds = new List<Guid>(MessageCount);

        for (var index = 0; index < MessageCount; index++)
        {
            var messageId = Guid.NewGuid();

            await scheduler.AcceptAsync(InboxAcceptItem<ShipOrderCommand>.WithIdentity(
                new ShipOrderCommand
                {
                    OrderId = messageId,
                    IdempotencyKey = $"stress:{messageId:N}"
                },
                messageId)).ConfigureAwait(false);

            messageIds.Add(messageId);
        }

        var processingStore = provider.GetRequiredService<IInboxProcessingStore>();
        var dispatcher = provider.GetRequiredService<IInboxDispatcher>();
        var processorOptions = provider.GetRequiredService<InboxProcessorOptions>();
        var clock = provider.GetRequiredService<TimeProvider>();

        var processors = Enumerable.Range(0, WorkerCount)
            .Select(workerIndex => new PipelinedInboxProcessor(
                processingStore,
                processingStore,
                dispatcher,
                processorOptions with
                {
                    LeaseOwner = $"stress-worker-{workerIndex}",
                    DispatcherConcurrency = 2,
                    LeaseHeartbeatInterval = TimeSpan.FromMilliseconds(25)
                },
                clock,
                Array.Empty<IProcessorEnvelopeHook>()))
            .ToArray();

        var workerTasks = processors.Select(processor => RunUntilIdleAsync(processor)).ToArray();
        await Task.WhenAll(workerTasks).ConfigureAwait(false);

        foreach (var messageId in messageIds)
        {
            var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, messageId).ConfigureAwait(false);
            row.Should().NotBeNull();
            row!.Status.Should().Be(InboxStatus.Completed);
            row.AttemptCount.Should().BeInRange(1, MaxAttempts);

            var invocations = tracker.GetInvocationCount(messageId);
            invocations.Should().BeInRange(1, MaxAttempts);
        }

        tracker.TotalInvocations.Should().BeInRange(MessageCount, MessageCount * MaxAttempts);
        }
    }

    private static async Task RunUntilIdleAsync(IInboxProcessor processor)
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

    private static ServiceProvider BuildProvider(
        PostgreSqlFixture fixture,
        PostgreSqlInboxStoreOptions options,
        InvocationTracker tracker)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tracker);

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddCommandModule(builder =>
            {
                builder.Register<ShipOrderCommand>();
                builder.Register<SlowShipOrderCommandHandler>();
            });

            registry.AddInboxModule(builder =>
            {
                builder.UsePostgreSqlStorage(postgres =>
                {
                    postgres.UseDataSource(fixture.DataSource);
                    postgres.UseOptions(options);
                });

                builder.Contracts.Register<ShipOrderCommand>("orders.commands.ship");

                builder.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 8,
                    LeaseDuration = TimeSpan.FromMilliseconds(75),
                    Retry = new RetryOptions
                    {
                        MaxAttempts = MaxAttempts,
                        UseJitter = false,
                        InitialDelay = TimeSpan.FromMilliseconds(10)
                    }
                });

                builder.UseInProcessDispatch();
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Tracks handler invocations per inbox message identifier.
    /// </summary>
    private sealed class InvocationTracker
    {
        private readonly ConcurrentDictionary<Guid, int> _invocations = new();

        public int TotalInvocations => _invocations.Values.Sum();

        public void Record(Guid messageId)
        {
            _invocations.AddOrUpdate(messageId, 1, static (_, count) => count + 1);
        }

        public int GetInvocationCount(Guid messageId)
        {
            return _invocations.TryGetValue(messageId, out var count) ? count : 0;
        }
    }

    /// <summary>
    ///     Handler that simulates slow work so leases expire under parallel workers.
    /// </summary>
    private sealed class SlowShipOrderCommandHandler : ICommandHandler<ShipOrderCommand>
    {
        private readonly InvocationTracker _tracker;

        public SlowShipOrderCommandHandler(InvocationTracker tracker)
        {
            _tracker = tracker;
        }

        public async Task HandleAsync(ShipOrderCommand message, CancellationToken cancellationToken = default)
        {
            _tracker.Record(message.OrderId);
            await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken).ConfigureAwait(false);
        }
    }
}
