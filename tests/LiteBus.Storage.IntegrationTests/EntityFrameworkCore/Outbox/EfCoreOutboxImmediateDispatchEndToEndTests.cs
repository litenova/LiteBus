using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
using LiteBus.Storage.IntegrationTests.EntityFrameworkCore;
using LiteBus.Storage.PostgreSql;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

/// <summary>
///     End-to-end immediate dispatch tests with Entity Framework Core storage and PostgreSQL.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class EfCoreOutboxImmediateDispatchEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const string ContractName = "orders.events.immediate-submitted";
    private const string ProjectionHandlerName = "projection";
    private const string AuditHandlerName = "audit";
    private static readonly TimeSpan EndToEndTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(30);

    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxImmediateDispatchEndToEndTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreOutboxImmediateDispatchEndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies that the projection an immediate handler writes is readable when <c>SendAsync</c> returns and that
    ///     each healthy handler runs exactly once, counted in the handler runs table.
    /// </summary>
    /// <returns>A task that completes when the projection and the run counts are verified.</returns>
    [SkippableFact]
    public async Task SendAsync_ShouldProjectImmediatelyAndRunEachHandlerOnce()
    {
        var tables = await CreateTablesAsync().ConfigureAwait(false);
        var provider = BuildProvider(tables, new ImmediateDispatchComposition { LeaseOwner = "immediate-dispatch-happy-path" });
        await using (provider.ConfigureAwait(false))
        {
            using var runCts = new CancellationTokenSource(EndToEndTimeout);
            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);

            try
            {
                var orderId = Guid.NewGuid();
                var receipt = await provider.GetRequiredService<ICommandMediator>()
                    .SendAsync(new SubmitOrderCommand(orderId, 42m), cancellationToken: runCts.Token).ConfigureAwait(false);

                receipt.Should().Be($"receipt:{orderId}");

                var factory = provider.GetRequiredService<IDbContextFactory<ImmediateDispatchDbContext>>();

                var order = await ReadOrderAsync(factory, orderId).ConfigureAwait(false);
                order.Should().NotBeNull();
                order!.Amount.Should().Be(42m);

                var projection = await ReadProjectionAsync(factory, orderId).ConfigureAwait(false);
                projection.Should().NotBeNull();

                var rows = await ReadOutboxRowsAsync(factory).ConfigureAwait(false);
                rows.Should().ContainSingle();
                rows[0].Status.Should().Be(OutboxStatus.Published);
                rows[0].AttemptCount.Should().Be(1);

                (await CountRunsAsync(factory, orderId, ProjectionHandlerName).ConfigureAwait(false)).Should().Be(1);
                (await CountRunsAsync(factory, orderId, AuditHandlerName).ConfigureAwait(false)).Should().Be(1);
            }
            finally
            {
                await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Verifies that a failing immediate handler leaves the command successful and that the outbox processor
    ///     publishes the envelope later with the processor retry visibility.
    /// </summary>
    /// <returns>A task that completes when the retry is published and verified.</returns>
    [SkippableFact]
    public async Task SendAsync_WhenImmediateHandlerFails_ShouldSucceedAndLetProcessorPublishLater()
    {
        var tables = await CreateTablesAsync().ConfigureAwait(false);
        var provider = BuildProvider(tables, new ImmediateDispatchComposition
        {
            LeaseOwner = "immediate-dispatch-retry",
            RetryInitialDelay = TimeSpan.FromSeconds(5),
            IncludeAuditHandler = false
        });
        await using (provider.ConfigureAwait(false))
        {
            using var runCts = new CancellationTokenSource(EndToEndTimeout);
            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);

            try
            {
                var orderId = Guid.NewGuid();
                var factory = provider.GetRequiredService<IDbContextFactory<ImmediateDispatchDbContext>>();
                await SeedControlAsync(factory, orderId, failuresRemaining: 1, delayMilliseconds: 0).ConfigureAwait(false);

                var before = DateTimeOffset.UtcNow;
                var receipt = await provider.GetRequiredService<ICommandMediator>()
                    .SendAsync(new SubmitOrderCommand(orderId, 42m), cancellationToken: runCts.Token).ConfigureAwait(false);

                receipt.Should().Be($"receipt:{orderId}");

                var projection = await ReadProjectionAsync(factory, orderId).ConfigureAwait(false);
                projection.Should().BeNull();

                var rows = await ReadOutboxRowsAsync(factory).ConfigureAwait(false);
                rows.Should().ContainSingle();
                rows[0].Status.Should().Be(OutboxStatus.Failed);
                rows[0].AttemptCount.Should().Be(1);
                rows[0].VisibleAfter.Should().NotBeNull();

                var visibleAfter = rows[0].VisibleAfter!.Value;
                (visibleAfter > before).Should().BeTrue("the failed envelope waits for the processor retry");
                (visibleAfter < before.AddSeconds(20)).Should().BeTrue("the failed envelope uses retry visibility, not the fallback delay");

                await WaitUntilAsync(
                    async () => (await ReadProjectionAsync(factory, orderId).ConfigureAwait(false)) is not null,
                    DeliveryTimeout,
                    runCts.Token).ConfigureAwait(false);

                (await CountRunsAsync(factory, orderId, ProjectionHandlerName).ConfigureAwait(false)).Should().Be(1);

                var settled = await ReadOutboxRowsAsync(factory).ConfigureAwait(false);
                settled.Should().ContainSingle();
                settled[0].Status.Should().Be(OutboxStatus.Published);
                settled[0].AttemptCount.Should().Be(2);
            }
            finally
            {
                await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Verifies that a commit listener registered before immediate dispatch can throw a simulated crash without
    ///     failing the command and that the envelope is still delivered exactly once.
    /// </summary>
    /// <returns>A task that completes when the delivery after the crash is verified.</returns>
    [SkippableFact]
    public async Task SendAsync_WhenCommitListenerBeforeImmediateDispatchThrows_ShouldStillSucceedAndDeliverOnce()
    {
        var tables = await CreateTablesAsync().ConfigureAwait(false);
        var provider = BuildProvider(tables, new ImmediateDispatchComposition
        {
            LeaseOwner = "immediate-dispatch-crash",
            CommitListeners = [typeof(SimulatedCrashCommitListener)],
            TrailingCommitListeners = [typeof(RecordingCommitListener)]
        });
        await using (provider.ConfigureAwait(false))
        {
            using var runCts = new CancellationTokenSource(EndToEndTimeout);
            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);

            try
            {
                var orderId = Guid.NewGuid();
                var receipt = await provider.GetRequiredService<ICommandMediator>()
                    .SendAsync(new SubmitOrderCommand(orderId, 42m), cancellationToken: runCts.Token).ConfigureAwait(false);

                receipt.Should().Be($"receipt:{orderId}");

                var sequence = provider.GetRequiredService<CommitListenerSequence>();
                sequence.Entries.Should().Equal("crash", "recorded");

                var factory = provider.GetRequiredService<IDbContextFactory<ImmediateDispatchDbContext>>();

                var projection = await ReadProjectionAsync(factory, orderId).ConfigureAwait(false);
                projection.Should().NotBeNull();

                var rows = await ReadOutboxRowsAsync(factory).ConfigureAwait(false);
                rows.Should().ContainSingle();
                rows[0].Status.Should().Be(OutboxStatus.Published);
                rows[0].AttemptCount.Should().Be(1);

                (await CountRunsAsync(factory, orderId, ProjectionHandlerName).ConfigureAwait(false)).Should().Be(1);
                (await CountRunsAsync(factory, orderId, AuditHandlerName).ConfigureAwait(false)).Should().Be(1);
            }
            finally
            {
                await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Verifies that an immediate envelope whose dispatch never started stays invisible to the processor during the
    ///     fallback delay and is delivered by the processor once the delay passes.
    /// </summary>
    /// <returns>A task that completes when the fallback delivery is verified.</returns>
    [SkippableFact]
    public async Task EnqueueAsync_WhenImmediateDispatchNeverStarted_ShouldBeDeliveredByProcessorAfterFallbackDelay()
    {
        var tables = await CreateTablesAsync().ConfigureAwait(false);
        var provider = BuildProvider(tables, new ImmediateDispatchComposition
        {
            LeaseOwner = "immediate-dispatch-fallback",
            ConfigureImmediateDispatch = options =>
            {
                options.Timeout = TimeSpan.FromSeconds(1);
                options.FallbackDelay = TimeSpan.FromSeconds(3);
            }
        });
        await using (provider.ConfigureAwait(false))
        {
            using var runCts = new CancellationTokenSource(EndToEndTimeout);
            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);

            try
            {
                var messageId = Guid.NewGuid();
                var orderId = Guid.NewGuid();
                var factory = provider.GetRequiredService<IDbContextFactory<ImmediateDispatchDbContext>>();

                var before = DateTimeOffset.UtcNow;
                await provider.GetRequiredService<IOutbox>().EnqueueAsync(
                    OutboxEnqueueItem<OrderSubmittedImmediateEvent>.WithIdentity(
                        new OrderSubmittedImmediateEvent { OrderId = orderId },
                        messageId),
                    runCts.Token).ConfigureAwait(false);

                var rows = await ReadOutboxRowsAsync(factory).ConfigureAwait(false);
                rows.Should().ContainSingle();
                rows[0].Id.Should().Be(messageId);
                rows[0].Status.Should().Be(OutboxStatus.Pending);
                rows[0].VisibleAfter.Should().NotBeNull();

                var visibleAfter = rows[0].VisibleAfter!.Value;
                (visibleAfter > before.AddSeconds(1)).Should().BeTrue("the immediate envelope stays invisible during the fallback delay");
                (visibleAfter < before.AddSeconds(10)).Should().BeTrue("the immediate envelope becomes visible after the fallback delay");

                await Task.Delay(TimeSpan.FromMilliseconds(1500), runCts.Token).ConfigureAwait(false);

                (await CountRunsAsync(factory, orderId, ProjectionHandlerName).ConfigureAwait(false)).Should().Be(0);
                (await CountRunsAsync(factory, orderId, AuditHandlerName).ConfigureAwait(false)).Should().Be(0);

                var stillPending = await ReadOutboxRowsAsync(factory).ConfigureAwait(false);
                stillPending.Should().ContainSingle();
                stillPending[0].Status.Should().Be(OutboxStatus.Pending);

                await WaitUntilAsync(
                    async () => (await ReadProjectionAsync(factory, orderId).ConfigureAwait(false)) is not null,
                    DeliveryTimeout,
                    runCts.Token).ConfigureAwait(false);

                (await CountRunsAsync(factory, orderId, ProjectionHandlerName).ConfigureAwait(false)).Should().Be(1);
                (await CountRunsAsync(factory, orderId, AuditHandlerName).ConfigureAwait(false)).Should().Be(1);

                var settled = await ReadOutboxRowsAsync(factory).ConfigureAwait(false);
                settled.Should().ContainSingle();
                settled[0].Status.Should().Be(OutboxStatus.Published);
                settled[0].AttemptCount.Should().Be(1);
            }
            finally
            {
                await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Verifies that two hosts sharing one outbox table dispatch each immediate envelope exactly once while both
    ///     processors poll.
    /// </summary>
    /// <returns>A task that completes when every order is delivered once on each handler.</returns>
    [SkippableFact]
    public async Task SendAsync_WithTwoHostsSharingOneTable_ShouldDispatchEachEnvelopeOnce()
    {
        var tables = await CreateTablesAsync().ConfigureAwait(false);
        var providerA = BuildProvider(tables, new ImmediateDispatchComposition { LeaseOwner = "immediate-dispatch-host-a" });
        await using (providerA.ConfigureAwait(false))
        {
            var providerB = BuildProvider(tables, new ImmediateDispatchComposition
            {
                LeaseOwner = "immediate-dispatch-host-b",
                IncludeCommands = false
            });
            await using (providerB.ConfigureAwait(false))
            {
                using var runCts = new CancellationTokenSource(EndToEndTimeout);
                await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(providerA, runCts.Token).ConfigureAwait(false);
                await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(providerB, runCts.Token).ConfigureAwait(false);

                try
                {
                    var mediator = providerA.GetRequiredService<ICommandMediator>();
                    var factory = providerA.GetRequiredService<IDbContextFactory<ImmediateDispatchDbContext>>();
                    List<Guid> orderIds = [];

                    for (var index = 0; index < 5; index++)
                    {
                        var orderId = Guid.NewGuid();
                        orderIds.Add(orderId);

                        await mediator.SendAsync(
                            new SubmitOrderCommand(orderId, 10m + index),
                            cancellationToken: runCts.Token).ConfigureAwait(false);
                    }

                    await WaitUntilAsync(
                        async () => (await CountProjectionsAsync(factory, orderIds).ConfigureAwait(false)) == orderIds.Count,
                        DeliveryTimeout,
                        runCts.Token).ConfigureAwait(false);

                    foreach (var orderId in orderIds)
                    {
                        (await CountRunsAsync(factory, orderId, ProjectionHandlerName).ConfigureAwait(false)).Should().Be(1);
                        (await CountRunsAsync(factory, orderId, AuditHandlerName).ConfigureAwait(false)).Should().Be(1);
                    }

                    var rows = await ReadOutboxRowsAsync(factory).ConfigureAwait(false);
                    rows.Should().HaveCount(5);
                    rows.Should().OnlyContain(row => row.Status == OutboxStatus.Published);
                    rows.Should().OnlyContain(row => row.AttemptCount == 1);
                }
                finally
                {
                    await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(providerB, CancellationToken.None).ConfigureAwait(false);
                    await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(providerA, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    ///     Verifies that a command batch triggers one immediate dispatch covering every command: both projections are
    ///     readable when <c>SendManyAsync</c> returns and the commit listeners run once.
    /// </summary>
    /// <returns>A task that completes when both commands are delivered once on each handler.</returns>
    [SkippableFact]
    public async Task SendManyAsync_ShouldTriggerOneImmediateDispatchCoveringEveryCommand()
    {
        var tables = await CreateTablesAsync().ConfigureAwait(false);
        var provider = BuildProvider(tables, new ImmediateDispatchComposition
        {
            LeaseOwner = "immediate-dispatch-batch",
            CommitListeners = [typeof(RecordingCommitListener)]
        });
        await using (provider.ConfigureAwait(false))
        {
            using var runCts = new CancellationTokenSource(EndToEndTimeout);
            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);

            try
            {
                var firstOrderId = Guid.NewGuid();
                var secondOrderId = Guid.NewGuid();
                IReadOnlyList<ICommand> commands =
                [
                    new SubmitOrderCommand(firstOrderId, 11m),
                    new SubmitOrderCommand(secondOrderId, 22m)
                ];

                await provider.GetRequiredService<ICommandBatchMediator>()
                    .SendManyAsync(commands, cancellationToken: runCts.Token).ConfigureAwait(false);

                var sequence = provider.GetRequiredService<CommitListenerSequence>();
                sequence.Entries.Should().Equal("recorded");

                var factory = provider.GetRequiredService<IDbContextFactory<ImmediateDispatchDbContext>>();

                var firstProjection = await ReadProjectionAsync(factory, firstOrderId).ConfigureAwait(false);
                firstProjection.Should().NotBeNull();

                var secondProjection = await ReadProjectionAsync(factory, secondOrderId).ConfigureAwait(false);
                secondProjection.Should().NotBeNull();

                var rows = await ReadOutboxRowsAsync(factory).ConfigureAwait(false);
                rows.Should().HaveCount(2);
                rows.Should().OnlyContain(row => row.Status == OutboxStatus.Published);
                rows.Should().OnlyContain(row => row.AttemptCount == 1);

                (await CountRunsAsync(factory, firstOrderId, ProjectionHandlerName).ConfigureAwait(false)).Should().Be(1);
                (await CountRunsAsync(factory, firstOrderId, AuditHandlerName).ConfigureAwait(false)).Should().Be(1);
                (await CountRunsAsync(factory, secondOrderId, ProjectionHandlerName).ConfigureAwait(false)).Should().Be(1);
                (await CountRunsAsync(factory, secondOrderId, AuditHandlerName).ConfigureAwait(false)).Should().Be(1);
            }
            finally
            {
                await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Verifies that an expired immediate dispatch timeout releases the remaining envelopes for the processor with
    ///     immediate visibility instead of the fallback delay.
    /// </summary>
    /// <returns>A task that completes when the released envelope is delivered by the processor.</returns>
    [SkippableFact]
    public async Task SendAsync_WhenTimeoutExpires_ShouldReleaseRemainingEnvelopesForProcessor()
    {
        var tables = await CreateTablesAsync().ConfigureAwait(false);
        var provider = BuildProvider(tables, new ImmediateDispatchComposition
        {
            LeaseOwner = "immediate-dispatch-timeout",
            ConfigureImmediateDispatch = options =>
            {
                options.Timeout = TimeSpan.FromSeconds(2);
                options.FallbackDelay = TimeSpan.FromMinutes(10);
            }
        });
        await using (provider.ConfigureAwait(false))
        {
            using var runCts = new CancellationTokenSource(EndToEndTimeout);

            var firstOrderId = Guid.NewGuid();
            var secondOrderId = Guid.NewGuid();
            var factory = provider.GetRequiredService<IDbContextFactory<ImmediateDispatchDbContext>>();
            await SeedControlAsync(factory, firstOrderId, failuresRemaining: 0, delayMilliseconds: 6000).ConfigureAwait(false);

            var before = DateTimeOffset.UtcNow;
            var receipt = await provider.GetRequiredService<ICommandMediator>()
                .SendAsync(new SubmitOrderPairCommand(firstOrderId, secondOrderId, 42m), cancellationToken: runCts.Token).ConfigureAwait(false);

            receipt.Should().Be($"receipt:{firstOrderId}:{secondOrderId}");

            var firstProjection = await ReadProjectionAsync(factory, firstOrderId).ConfigureAwait(false);
            firstProjection.Should().NotBeNull();

            var secondProjection = await ReadProjectionAsync(factory, secondOrderId).ConfigureAwait(false);
            secondProjection.Should().BeNull();

            var rows = await ReadOutboxRowsAsync(factory).ConfigureAwait(false);
            rows.Should().HaveCount(2);

            var first = rows.Single(row => row.Payload.Contains(firstOrderId.ToString(), StringComparison.OrdinalIgnoreCase));
            var second = rows.Single(row => row.Payload.Contains(secondOrderId.ToString(), StringComparison.OrdinalIgnoreCase));

            first.Status.Should().Be(OutboxStatus.Published);
            first.AttemptCount.Should().Be(1);

            second.Status.Should().Be(OutboxStatus.Pending);
            second.AttemptCount.Should().Be(0);
            second.VisibleAfter.Should().NotBeNull();

            var releasedAt = second.VisibleAfter!.Value;
            (releasedAt >= before).Should().BeTrue("the timed-out envelope is released after the dispatch started");
            (releasedAt <= DateTimeOffset.UtcNow.AddMinutes(1)).Should().BeTrue("the timed-out envelope is released for the processor instead of waiting for the fallback delay");

            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);

            try
            {
                await WaitUntilAsync(
                    async () => (await ReadProjectionAsync(factory, secondOrderId).ConfigureAwait(false)) is not null,
                    DeliveryTimeout,
                    runCts.Token).ConfigureAwait(false);

                (await CountRunsAsync(factory, firstOrderId, ProjectionHandlerName).ConfigureAwait(false)).Should().Be(1);
                (await CountRunsAsync(factory, firstOrderId, AuditHandlerName).ConfigureAwait(false)).Should().Be(1);
                (await CountRunsAsync(factory, secondOrderId, ProjectionHandlerName).ConfigureAwait(false)).Should().Be(1);
                (await CountRunsAsync(factory, secondOrderId, AuditHandlerName).ConfigureAwait(false)).Should().Be(1);

                var settled = await ReadOutboxRowsAsync(factory).ConfigureAwait(false);
                settled.Should().HaveCount(2);
                settled.Should().OnlyContain(row => row.Status == OutboxStatus.Published);
            }
            finally
            {
                await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Builds one host provider over the test tables with the given immediate dispatch composition.
    /// </summary>
    /// <param name="tables">The outbox store options and domain table names for one test run.</param>
    /// <param name="composition">The composition switches for one test.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(ImmediateDispatchTables tables, ImmediateDispatchComposition composition)
    {
        var services = new ServiceCollection();
        var connectionString = EfCoreProviderTestInfrastructure.CreateOutboxScopedConnectionString(_fixture.ConnectionString, tables.StoreOptions);

        services.AddSingleton(tables.StoreOptions);
        services.AddSingleton(tables.TableNames);
        services.AddSingleton<CommitListenerSequence>();

        foreach (var listenerType in composition.CommitListeners)
        {
            services.AddScoped(typeof(IUnitOfWorkCommitListener), listenerType);
        }

        if (composition.IncludeCommands)
        {
            services.AddDbContext<ImmediateDispatchDbContext>((provider, options) =>
                ConfigureTransactionalOptions(provider, options, connectionString));
            services.AddDbContextFactory<ImmediateDispatchDbContext>((provider, options) =>
                ConfigureTransactionalOptions(provider, options, connectionString));
        }
        else
        {
            services.AddDbContextFactory<ImmediateDispatchDbContext>(options =>
                options.UseNpgsql(connectionString).ReplaceService<IModelCacheKeyFactory, TableNameModelCacheKeyFactory>());
        }

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            if (composition.IncludeCommands)
            {
                registry.AddCommands(commands =>
                {
                    commands.Register<SubmitOrderCommand>();
                    commands.Register<SubmitOrderCommandHandler>();
                    commands.Register<SubmitOrderPairCommand>();
                    commands.Register<SubmitOrderPairCommandHandler>();
                    commands.UseUnitOfWork<ImmediateDispatchUnitOfWork>();
                });
            }

            registry.AddEvents(events =>
            {
                events.Register<OrderProjectionHandler>();

                if (composition.IncludeAuditHandler)
                {
                    events.Register<OrderAuditHandler>();
                }
            });

            registry.AddOutbox(outbox =>
            {
                outbox.UseEntityFrameworkCoreStorage(storage =>
                {
                    storage.UseDbContext<ImmediateDispatchDbContext>();
                    storage.UseOptions(tables.StoreOptions);

                    if (composition.IncludeCommands)
                    {
                        storage.EnableSaveChangesInterceptor();
                    }
                });

                outbox.Contracts.Register<OrderSubmittedImmediateEvent>(ContractName);

                outbox.UseProcessorOptions(new OutboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = composition.LeaseOwner,
                    Retry = new RetryOptions
                    {
                        MaxAttempts = 5,
                        InitialDelay = composition.RetryInitialDelay,
                        Backoff = RetryBackoff.Fixed,
                        UseJitter = false
                    }
                });

                outbox.EnableOutboxProcessor(host => host.PollInterval = TimeSpan.FromMilliseconds(100));

                if (composition.IncludeCommands)
                {
                    outbox.StageCollectedEvents();
                    outbox.EnableImmediateDispatch(composition.ConfigureImmediateDispatch);
                }

                outbox.UseInProcessDispatch();
            });
        });

        foreach (var listenerType in composition.TrailingCommitListeners)
        {
            services.AddScoped(typeof(IUnitOfWorkCommitListener), listenerType);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Configures database context options with the shared save-changes interceptor.
    /// </summary>
    /// <param name="provider">The service provider that resolves the interceptor.</param>
    /// <param name="options">The options builder being configured.</param>
    /// <param name="connectionString">The connection string scoped to one test run.</param>
    private static void ConfigureTransactionalOptions(IServiceProvider provider, DbContextOptionsBuilder options, string connectionString)
    {
        options.UseNpgsql(connectionString).ReplaceService<IModelCacheKeyFactory, TableNameModelCacheKeyFactory>();
        options.AddLiteBusOutboxInterceptor(provider.GetRequiredService<LiteBusOutboxSaveChangesInterceptor>());
    }

    /// <summary>
    ///     Creates the outbox and domain tables for one test run.
    /// </summary>
    /// <returns>The outbox store options and domain table names.</returns>
    private async Task<ImmediateDispatchTables> CreateTablesAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var tables = new ImmediateDispatchTables(
            EfCoreOutboxE2eSupport.CreateStoreOptions($"outbox_immediate_{suffix}"),
            new ImmediateDispatchTableNames(
                $"orders_immediate_{suffix}",
                $"projections_immediate_{suffix}",
                $"runs_immediate_{suffix}",
                $"controls_immediate_{suffix}"));

        await EfCoreOutboxE2eSupport.EnsureOutboxTableAsync(_fixture.ConnectionString, tables.StoreOptions).ConfigureAwait(false);
        await EnsureDomainTablesAsync(tables).ConfigureAwait(false);

        return tables;
    }

    /// <summary>
    ///     Creates the domain orders, projections, handler runs, and dispatch control tables for one test run.
    /// </summary>
    /// <param name="tables">The outbox store options and domain table names for one test run.</param>
    /// <returns>A task that completes when the tables exist.</returns>
    private async Task EnsureDomainTablesAsync(ImmediateDispatchTables tables)
    {
        var builder = new DbContextOptionsBuilder<ImmediateDispatchDbContext>()
            .UseNpgsql(_fixture.ConnectionString);

        var context = new ImmediateDispatchDbContext(builder.Options, tables.StoreOptions, tables.TableNames);
        await using (context.ConfigureAwait(false))
        {
            var quotedSchema = PostgreSqlIdentifier.Quote(EfCoreProviderTestInfrastructure.SchemaName);
            var qualifiedOrdersTable = PostgreSqlIdentifier.Qualify(EfCoreProviderTestInfrastructure.SchemaName, tables.TableNames.OrdersTableName);
            var qualifiedProjectionsTable = PostgreSqlIdentifier.Qualify(EfCoreProviderTestInfrastructure.SchemaName, tables.TableNames.ProjectionsTableName);
            var qualifiedRunsTable = PostgreSqlIdentifier.Qualify(EfCoreProviderTestInfrastructure.SchemaName, tables.TableNames.HandlerRunsTableName);
            var qualifiedControlsTable = PostgreSqlIdentifier.Qualify(EfCoreProviderTestInfrastructure.SchemaName, tables.TableNames.DispatchControlsTableName);

#pragma warning disable EF1002 // PostgreSqlIdentifier validates and quotes the generated schema and table identifiers.
            await context.Database.ExecuteSqlRawAsync(
                $"""
                 CREATE SCHEMA IF NOT EXISTS {quotedSchema};
                 CREATE TABLE IF NOT EXISTS {qualifiedOrdersTable} (
                     order_id uuid NOT NULL PRIMARY KEY,
                     amount numeric NOT NULL);
                 CREATE TABLE IF NOT EXISTS {qualifiedProjectionsTable} (
                     order_id uuid NOT NULL PRIMARY KEY,
                     projected_at timestamptz NOT NULL);
                 CREATE TABLE IF NOT EXISTS {qualifiedRunsTable} (
                     run_id uuid NOT NULL PRIMARY KEY,
                     order_id uuid NOT NULL,
                     handler_name text NOT NULL,
                     ran_at timestamptz NOT NULL);
                 CREATE TABLE IF NOT EXISTS {qualifiedControlsTable} (
                     order_id uuid NOT NULL PRIMARY KEY,
                     failures_remaining integer NOT NULL,
                     delay_ms integer NOT NULL);
                 """).ConfigureAwait(false);
#pragma warning restore EF1002
        }
    }

    /// <summary>
    ///     Waits until the supplied condition becomes true or the timeout elapses.
    /// </summary>
    /// <param name="condition">The condition to poll.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <param name="cancellationToken">The token used to cancel waiting.</param>
    /// <returns>A task that completes when the condition is satisfied.</returns>
    private static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await condition().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        (await condition().ConfigureAwait(false)).Should().BeTrue($"condition was not satisfied within {timeout}");
    }

    /// <summary>
    ///     Loads one domain order outside any mediation.
    /// </summary>
    /// <param name="factory">The factory that creates verification contexts.</param>
    /// <param name="orderId">The order identifier.</param>
    /// <returns>The stored row, or <see langword="null" /> when no row carries the identifier.</returns>
    private static async Task<ImmediateDispatchOrderEntity?> ReadOrderAsync(
        IDbContextFactory<ImmediateDispatchDbContext> factory,
        Guid orderId)
    {
        var context = await factory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
        await using var configuredContext = context.ConfigureAwait(false);

        return await context.Orders.SingleOrDefaultAsync(order => order.OrderId == orderId, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    ///     Loads one order projection outside any mediation.
    /// </summary>
    /// <param name="factory">The factory that creates verification contexts.</param>
    /// <param name="orderId">The order identifier.</param>
    /// <returns>The stored row, or <see langword="null" /> when no row carries the identifier.</returns>
    private static async Task<OrderProjectionEntity?> ReadProjectionAsync(
        IDbContextFactory<ImmediateDispatchDbContext> factory,
        Guid orderId)
    {
        var context = await factory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
        await using var configuredContext = context.ConfigureAwait(false);

        return await context.Projections.SingleOrDefaultAsync(projection => projection.OrderId == orderId, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    ///     Counts the projections stored for the given orders.
    /// </summary>
    /// <param name="factory">The factory that creates verification contexts.</param>
    /// <param name="orderIds">The order identifiers to count projections for.</param>
    /// <returns>The number of stored projections.</returns>
    private static async Task<int> CountProjectionsAsync(
        IDbContextFactory<ImmediateDispatchDbContext> factory,
        IReadOnlyList<Guid> orderIds)
    {
        var context = await factory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
        await using var configuredContext = context.ConfigureAwait(false);

        return await context.Projections.CountAsync(projection => orderIds.Contains(projection.OrderId)).ConfigureAwait(false);
    }

    /// <summary>
    ///     Counts the recorded runs of one handler for one order.
    /// </summary>
    /// <param name="factory">The factory that creates verification contexts.</param>
    /// <param name="orderId">The order identifier.</param>
    /// <param name="handlerName">The handler name recorded with each run.</param>
    /// <returns>The number of recorded runs.</returns>
    private static async Task<int> CountRunsAsync(
        IDbContextFactory<ImmediateDispatchDbContext> factory,
        Guid orderId,
        string handlerName)
    {
        var context = await factory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
        await using var configuredContext = context.ConfigureAwait(false);

        return await context.HandlerRuns.CountAsync(run => run.OrderId == orderId && run.HandlerName == handlerName).ConfigureAwait(false);
    }

    /// <summary>
    ///     Reads every outbox row in creation order.
    /// </summary>
    /// <param name="factory">The factory that creates verification contexts.</param>
    /// <returns>The stored outbox rows.</returns>
    private static async Task<IReadOnlyList<OutboxMessageEntity>> ReadOutboxRowsAsync(
        IDbContextFactory<ImmediateDispatchDbContext> factory)
    {
        var context = await factory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
        await using var configuredContext = context.ConfigureAwait(false);

        return await context.OutboxMessages.OrderBy(message => message.CreatedAt).ToListAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Seeds one dispatch control row that steers the event handlers for one order.
    /// </summary>
    /// <param name="factory">The factory that creates verification contexts.</param>
    /// <param name="orderId">The order identifier.</param>
    /// <param name="failuresRemaining">The number of times the projection handler throws before succeeding.</param>
    /// <param name="delayMilliseconds">The time the handlers wait before recording their runs.</param>
    /// <returns>A task that completes when the row is stored.</returns>
    private static async Task SeedControlAsync(
        IDbContextFactory<ImmediateDispatchDbContext> factory,
        Guid orderId,
        int failuresRemaining,
        int delayMilliseconds)
    {
        var context = await factory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
        await using var configuredContext = context.ConfigureAwait(false);

        context.DispatchControls.Add(new DispatchControlEntity
        {
            OrderId = orderId,
            FailuresRemaining = failuresRemaining,
            DelayMilliseconds = delayMilliseconds
        });

        await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private sealed record ImmediateDispatchTableNames(
        string OrdersTableName,
        string ProjectionsTableName,
        string HandlerRunsTableName,
        string DispatchControlsTableName);

    private sealed record ImmediateDispatchTables(
        EntityFrameworkCoreOutboxStoreOptions StoreOptions,
        ImmediateDispatchTableNames TableNames);

    private sealed class ImmediateDispatchComposition
    {
        public Action<OutboxImmediateDispatchOptions>? ConfigureImmediateDispatch { get; init; }

        public TimeSpan RetryInitialDelay { get; init; } = TimeSpan.FromSeconds(1);

        public string LeaseOwner { get; init; } = "immediate-dispatch-e2e";

        public bool IncludeAuditHandler { get; init; } = true;

        public bool IncludeCommands { get; init; } = true;

        public IReadOnlyList<Type> CommitListeners { get; init; } = [];

        public IReadOnlyList<Type> TrailingCommitListeners { get; init; } = [];
    }

    /// <summary>
    ///     Keys the EF Core model cache by the outbox and domain table names, so each test's context maps its own
    ///     tables instead of reusing the model the first test built.
    /// </summary>
    private sealed class TableNameModelCacheKeyFactory : IModelCacheKeyFactory
    {
        /// <inheritdoc />
        public object Create(DbContext context, bool designTime)
        {
            return context is ImmediateDispatchDbContext immediate
                ? (context.GetType(), immediate.ModelKey, designTime)
                : (object) (context.GetType(), designTime);
        }
    }

    private sealed class ImmediateDispatchDbContext : DbContext, IOutboxDbContext
    {
        private readonly EntityFrameworkCoreOutboxStoreOptions _storeOptions;
        private readonly ImmediateDispatchTableNames _tables;

        public ImmediateDispatchDbContext(
            DbContextOptions<ImmediateDispatchDbContext> options,
            EntityFrameworkCoreOutboxStoreOptions storeOptions,
            ImmediateDispatchTableNames tables)
            : base(options)
        {
            _storeOptions = storeOptions;
            _tables = tables;
        }

        public string ModelKey => _storeOptions.SchemaName + "." + _storeOptions.TableName + "|" + _tables;

        public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

        public DbSet<ImmediateDispatchOrderEntity> Orders => Set<ImmediateDispatchOrderEntity>();

        public DbSet<OrderProjectionEntity> Projections => Set<OrderProjectionEntity>();

        public DbSet<HandlerRunEntity> HandlerRuns => Set<HandlerRunEntity>();

        public DbSet<DispatchControlEntity> DispatchControls => Set<DispatchControlEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration(_storeOptions, EfCoreStorageProvider.PostgreSql);

            modelBuilder.Entity<ImmediateDispatchOrderEntity>(entity =>
            {
                entity.ToTable(_tables.OrdersTableName, _storeOptions.SchemaName);
                entity.HasKey(order => order.OrderId);
                entity.Property(order => order.OrderId).HasColumnName("order_id");
                entity.Property(order => order.Amount).HasColumnName("amount").HasColumnType("numeric");
            });

            modelBuilder.Entity<OrderProjectionEntity>(entity =>
            {
                entity.ToTable(_tables.ProjectionsTableName, _storeOptions.SchemaName);
                entity.HasKey(projection => projection.OrderId);
                entity.Property(projection => projection.OrderId).HasColumnName("order_id");
                entity.Property(projection => projection.ProjectedAt).HasColumnName("projected_at");
            });

            modelBuilder.Entity<HandlerRunEntity>(entity =>
            {
                entity.ToTable(_tables.HandlerRunsTableName, _storeOptions.SchemaName);
                entity.HasKey(run => run.RunId);
                entity.Property(run => run.RunId).HasColumnName("run_id");
                entity.Property(run => run.OrderId).HasColumnName("order_id");
                entity.Property(run => run.HandlerName).HasColumnName("handler_name");
                entity.Property(run => run.RanAt).HasColumnName("ran_at");
            });

            modelBuilder.Entity<DispatchControlEntity>(entity =>
            {
                entity.ToTable(_tables.DispatchControlsTableName, _storeOptions.SchemaName);
                entity.HasKey(control => control.OrderId);
                entity.Property(control => control.OrderId).HasColumnName("order_id");
                entity.Property(control => control.FailuresRemaining).HasColumnName("failures_remaining");
                entity.Property(control => control.DelayMilliseconds).HasColumnName("delay_ms");
            });
        }
    }

    private sealed class ImmediateDispatchOrderEntity
    {
        public Guid OrderId { get; set; }

        public decimal Amount { get; set; }
    }

    private sealed class OrderProjectionEntity
    {
        public Guid OrderId { get; set; }

        public DateTimeOffset ProjectedAt { get; set; }
    }

    private sealed class HandlerRunEntity
    {
        public Guid RunId { get; set; }

        public Guid OrderId { get; set; }

        public string HandlerName { get; set; } = string.Empty;

        public DateTimeOffset RanAt { get; set; }
    }

    private sealed class DispatchControlEntity
    {
        public Guid OrderId { get; set; }

        public int FailuresRemaining { get; set; }

        public int DelayMilliseconds { get; set; }
    }

    private sealed class ImmediateDispatchUnitOfWork : IUnitOfWork
    {
        private readonly ImmediateDispatchDbContext _context;

        public ImmediateDispatchUnitOfWork(ImmediateDispatchDbContext context)
        {
            _context = context;
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record SubmitOrderCommand(Guid OrderId, decimal Amount) : ICommand<string>;

    private sealed class SubmitOrderCommandHandler : ICommandHandler<SubmitOrderCommand, string>
    {
        private readonly IDomainEventCollector _collector;
        private readonly ImmediateDispatchDbContext _context;

        public SubmitOrderCommandHandler(ImmediateDispatchDbContext context, IDomainEventCollector collector)
        {
            _context = context;
            _collector = collector;
        }

        public Task<string> HandleAsync(SubmitOrderCommand message, CancellationToken cancellationToken = default)
        {
            _context.Orders.Add(new ImmediateDispatchOrderEntity { OrderId = message.OrderId, Amount = message.Amount });
            _collector.Collect(new OrderSubmittedImmediateEvent { OrderId = message.OrderId });

            return Task.FromResult($"receipt:{message.OrderId}");
        }
    }

    private sealed record SubmitOrderPairCommand(Guid FirstOrderId, Guid SecondOrderId, decimal Amount) : ICommand<string>;

    private sealed class SubmitOrderPairCommandHandler : ICommandHandler<SubmitOrderPairCommand, string>
    {
        private readonly IDomainEventCollector _collector;
        private readonly ImmediateDispatchDbContext _context;

        public SubmitOrderPairCommandHandler(ImmediateDispatchDbContext context, IDomainEventCollector collector)
        {
            _context = context;
            _collector = collector;
        }

        public Task<string> HandleAsync(SubmitOrderPairCommand message, CancellationToken cancellationToken = default)
        {
            // The first event carries the delay control, so staging order decides which envelope the timeout releases.
            _context.Orders.Add(new ImmediateDispatchOrderEntity { OrderId = message.FirstOrderId, Amount = message.Amount });
            _context.Orders.Add(new ImmediateDispatchOrderEntity { OrderId = message.SecondOrderId, Amount = message.Amount });
            _collector.Collect(new OrderSubmittedImmediateEvent { OrderId = message.FirstOrderId });
            _collector.Collect(new OrderSubmittedImmediateEvent { OrderId = message.SecondOrderId });

            return Task.FromResult($"receipt:{message.FirstOrderId}:{message.SecondOrderId}");
        }
    }

    [DispatchImmediately]
    private sealed record OrderSubmittedImmediateEvent
    {
        public Guid OrderId { get; init; }
    }

    private sealed class OrderProjectionHandler : IEventHandler<OrderSubmittedImmediateEvent>
    {
        private readonly IDbContextFactory<ImmediateDispatchDbContext> _factory;

        public OrderProjectionHandler(IDbContextFactory<ImmediateDispatchDbContext> factory)
        {
            _factory = factory;
        }

        public async Task HandleAsync(OrderSubmittedImmediateEvent message, CancellationToken cancellationToken = default)
        {
            // The handler ignores the dispatch token so a timeout expiring mid-flight cannot turn a completed
            // projection into a cancellation.
            var context = await _factory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
            await using var configuredContext = context.ConfigureAwait(false);
            var control = await context.DispatchControls.SingleOrDefaultAsync(control => control.OrderId == message.OrderId, CancellationToken.None).ConfigureAwait(false);

            if (control is not null && control.DelayMilliseconds > 0)
            {
                await Task.Delay(control.DelayMilliseconds, CancellationToken.None).ConfigureAwait(false);
            }

            if (control is not null && control.FailuresRemaining > 0)
            {
                control.FailuresRemaining--;
                await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

                throw new InvalidOperationException($"Simulated projection failure for order {message.OrderId}.");
            }

            context.Projections.Add(new OrderProjectionEntity { OrderId = message.OrderId, ProjectedAt = DateTimeOffset.UtcNow });
            context.HandlerRuns.Add(new HandlerRunEntity
            {
                RunId = Guid.NewGuid(),
                OrderId = message.OrderId,
                HandlerName = ProjectionHandlerName,
                RanAt = DateTimeOffset.UtcNow
            });

            await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private sealed class OrderAuditHandler : IEventHandler<OrderSubmittedImmediateEvent>
    {
        private readonly IDbContextFactory<ImmediateDispatchDbContext> _factory;

        public OrderAuditHandler(IDbContextFactory<ImmediateDispatchDbContext> factory)
        {
            _factory = factory;
        }

        public async Task HandleAsync(OrderSubmittedImmediateEvent message, CancellationToken cancellationToken = default)
        {
            var context = await _factory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
            await using var configuredContext = context.ConfigureAwait(false);
            var control = await context.DispatchControls.SingleOrDefaultAsync(control => control.OrderId == message.OrderId, CancellationToken.None).ConfigureAwait(false);

            if (control is not null && control.DelayMilliseconds > 0)
            {
                await Task.Delay(control.DelayMilliseconds, CancellationToken.None).ConfigureAwait(false);
            }

            context.HandlerRuns.Add(new HandlerRunEntity
            {
                RunId = Guid.NewGuid(),
                OrderId = message.OrderId,
                HandlerName = AuditHandlerName,
                RanAt = DateTimeOffset.UtcNow
            });

            await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private sealed class CommitListenerSequence
    {
        private readonly object _gate = new();
        private readonly List<string> _entries = [];

        public IReadOnlyList<string> Entries
        {
            get
            {
                lock (_gate)
                {
                    return [.. _entries];
                }
            }
        }

        public void Record(string entry)
        {
            lock (_gate)
            {
                _entries.Add(entry);
            }
        }
    }

    private sealed class SimulatedCrashCommitListener : IUnitOfWorkCommitListener
    {
        private readonly CommitListenerSequence _sequence;

        public SimulatedCrashCommitListener(CommitListenerSequence sequence)
        {
            _sequence = sequence;
        }

        public Task OnCommittedAsync(CancellationToken cancellationToken = default)
        {
            _sequence.Record("crash");

            throw new InvalidOperationException("Simulated crash before immediate dispatch.");
        }
    }

    private sealed class RecordingCommitListener : IUnitOfWorkCommitListener
    {
        private readonly CommitListenerSequence _sequence;

        public RecordingCommitListener(CommitListenerSequence sequence)
        {
            _sequence = sequence;
        }

        public Task OnCommittedAsync(CancellationToken cancellationToken = default)
        {
            _sequence.Record("recorded");

            return Task.CompletedTask;
        }
    }
}
