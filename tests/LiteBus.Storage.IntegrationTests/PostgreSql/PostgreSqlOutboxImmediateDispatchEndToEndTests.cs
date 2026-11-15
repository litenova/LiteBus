using LiteBus.Commands;
using LiteBus.Events;
using LiteBus.Outbox;
using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

public sealed class PostgreSqlOutboxImmediateDispatchEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const string ProjectionHandlerName = "projection";
    private const string AuditHandlerName = "audit";
    private const string FlakyHandlerName = "flaky-projection";
    private const string BlockingHandlerName = "blocking-projection";
    private const decimal OrderAmount = 10m;
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(30);

    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlOutboxImmediateDispatchEndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task SendAsync_WhenImmediateHandlerSucceeds_ShouldMakeProjectionReadableOnReturn()
    {
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        var tables = CreateTables(outboxOptions);
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, outboxOptions).ConfigureAwait(false);
        await EnsureDomainTablesAsync(_fixture.DataSource, tables).ConfigureAwait(false);

        var provider = BuildProvider(tables, outboxOptions);
        await using (provider.ConfigureAwait(false))
        {
            using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);

            try
            {
                var orderId = Guid.NewGuid();
                var messageId = Guid.NewGuid();
                var auditMessageId = Guid.NewGuid();

                var mediator = provider.GetRequiredService<ICommandMediator>();
                var returned = await mediator.SendAsync(new SubmitImmediateOrderCommand(orderId, messageId, auditMessageId)).ConfigureAwait(false);
                returned.Should().Be(orderId);

                (await ReadProjectionExistsAsync(_fixture.DataSource, tables, orderId).ConfigureAwait(false)).Should().BeTrue();

                var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, outboxOptions, messageId).ConfigureAwait(false);
                row.Should().NotBeNull();
                row!.Status.Should().Be(OutboxStatus.Published);
                row.AttemptCount.Should().Be(1);

                (await CountRunsAsync(_fixture.DataSource, tables, orderId, ProjectionHandlerName).ConfigureAwait(false)).Should().Be(1);
            }
            finally
            {
                await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    [SkippableFact]
    public async Task SendAsync_WhenHealthyHandlersRun_ShouldRunEachHandlerExactlyOnce()
    {
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        var tables = CreateTables(outboxOptions);
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, outboxOptions).ConfigureAwait(false);
        await EnsureDomainTablesAsync(_fixture.DataSource, tables).ConfigureAwait(false);

        var provider = BuildProvider(tables, outboxOptions);
        await using (provider.ConfigureAwait(false))
        {
            using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);

            try
            {
                var submitted = new List<SubmitImmediateOrderCommand>(5);

                for (var index = 0; index < 5; index++)
                {
                    submitted.Add(new SubmitImmediateOrderCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
                }

                var mediator = provider.GetRequiredService<ICommandMediator>();

                foreach (var command in submitted)
                {
                    var returned = await mediator.SendAsync(command).ConfigureAwait(false);
                    returned.Should().Be(command.OrderId);
                }

                foreach (var command in submitted)
                {
                    (await ReadProjectionExistsAsync(_fixture.DataSource, tables, command.OrderId).ConfigureAwait(false)).Should().BeTrue();

                    var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, outboxOptions, command.MessageId).ConfigureAwait(false);
                    row.Should().NotBeNull();
                    row!.Status.Should().Be(OutboxStatus.Published);
                    row.AttemptCount.Should().Be(1);

                    (await CountRunsAsync(_fixture.DataSource, tables, command.OrderId, ProjectionHandlerName).ConfigureAwait(false)).Should().Be(1);
                }

                var auditMessageIds = submitted.Select(command => command.AuditMessageId).ToList();
                (await WaitUntilAllPublishedAsync(_fixture.DataSource, outboxOptions, auditMessageIds).ConfigureAwait(false)).Should().BeTrue();

                foreach (var command in submitted)
                {
                    var auditRow = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, outboxOptions, command.AuditMessageId).ConfigureAwait(false);
                    auditRow.Should().NotBeNull();
                    auditRow!.Status.Should().Be(OutboxStatus.Published);
                    auditRow.AttemptCount.Should().Be(1);

                    (await CountRunsAsync(_fixture.DataSource, tables, command.OrderId, AuditHandlerName).ConfigureAwait(false)).Should().Be(1);
                }

                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

                foreach (var command in submitted)
                {
                    (await CountRunsAsync(_fixture.DataSource, tables, command.OrderId, ProjectionHandlerName).ConfigureAwait(false)).Should().Be(1);
                    (await CountRunsAsync(_fixture.DataSource, tables, command.OrderId, AuditHandlerName).ConfigureAwait(false)).Should().Be(1);
                }
            }
            finally
            {
                await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    [SkippableFact]
    public async Task SendAsync_WhenImmediateHandlerFails_ShouldSucceedCommandAndPublishLaterThroughProcessor()
    {
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        var tables = CreateTables(outboxOptions);
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, outboxOptions).ConfigureAwait(false);
        await EnsureDomainTablesAsync(_fixture.DataSource, tables).ConfigureAwait(false);

        var provider = BuildProvider(tables, outboxOptions);
        await using (provider.ConfigureAwait(false))
        {
            using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);

            try
            {
                var orderId = Guid.NewGuid();
                var messageId = Guid.NewGuid();

                var mediator = provider.GetRequiredService<ICommandMediator>();
                var returned = await mediator.SendAsync(new SubmitFlakyOrderCommand(orderId, messageId)).ConfigureAwait(false);
                returned.Should().Be(orderId);

                var failed = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, outboxOptions, messageId).ConfigureAwait(false);
                failed.Should().NotBeNull();
                failed!.Status.Should().Be(OutboxStatus.Failed);

                (await ReadProjectionExistsAsync(_fixture.DataSource, tables, orderId).ConfigureAwait(false)).Should().BeFalse();
                (await CountRunsAsync(_fixture.DataSource, tables, orderId, FlakyHandlerName).ConfigureAwait(false)).Should().Be(1);
                (await CountSucceededRunsAsync(_fixture.DataSource, tables, orderId, FlakyHandlerName).ConfigureAwait(false)).Should().Be(0);

                (await WaitUntilAllPublishedAsync(_fixture.DataSource, outboxOptions, [messageId]).ConfigureAwait(false)).Should().BeTrue();

                (await ReadProjectionExistsAsync(_fixture.DataSource, tables, orderId).ConfigureAwait(false)).Should().BeTrue();
                (await CountRunsAsync(_fixture.DataSource, tables, orderId, FlakyHandlerName).ConfigureAwait(false)).Should().Be(2);
                (await CountSucceededRunsAsync(_fixture.DataSource, tables, orderId, FlakyHandlerName).ConfigureAwait(false)).Should().Be(1);

                var published = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, outboxOptions, messageId).ConfigureAwait(false);
                published.Should().NotBeNull();
                published!.Status.Should().Be(OutboxStatus.Published);
            }
            finally
            {
                await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    [SkippableFact]
    public async Task SendAsync_WhenCommitListenerThrowsBeforeImmediateDispatch_ShouldDeliverThroughProcessorAfterFallbackDelay()
    {
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        var tables = CreateTables(outboxOptions);
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, outboxOptions).ConfigureAwait(false);
        await EnsureDomainTablesAsync(_fixture.DataSource, tables).ConfigureAwait(false);

        var provider = BuildProvider(
            tables,
            outboxOptions,
            configureImmediateDispatch: options =>
            {
                options.Timeout = TimeSpan.FromSeconds(2);
                options.FallbackDelay = TimeSpan.FromSeconds(5);
            },
            configureServices: services =>
            {
                services.AddScoped<IUnitOfWorkCommitListener, SimulatedCrashCommitListener>();
            });
        await using (provider.ConfigureAwait(false))
        {
            using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);

            try
            {
                var orderId = Guid.NewGuid();
                var messageId = Guid.NewGuid();
                var auditMessageId = Guid.NewGuid();

                var mediator = provider.GetRequiredService<ICommandMediator>();
                var returned = await mediator.SendAsync(new SubmitImmediateOrderCommand(orderId, messageId, auditMessageId)).ConfigureAwait(false);
                returned.Should().Be(orderId);

                var held = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, outboxOptions, messageId).ConfigureAwait(false);
                held.Should().NotBeNull();
                held!.Status.Should().Be(OutboxStatus.Publishing);
                held.LeaseOwner.Should().Be(SimulatedCrashCommitListener.LeaseOwner);
                held.AttemptCount.Should().Be(1);

                await ResetStagedEnvelopesToPendingAsync(_fixture.DataSource, outboxOptions, [messageId, auditMessageId]).ConfigureAwait(false);

                (await WaitUntilAllPublishedAsync(_fixture.DataSource, outboxOptions, [auditMessageId]).ConfigureAwait(false)).Should().BeTrue();

                var immediate = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, outboxOptions, messageId).ConfigureAwait(false);
                immediate.Should().NotBeNull();
                immediate!.Status.Should().Be(OutboxStatus.Pending);
                (await CountRunsAsync(_fixture.DataSource, tables, orderId, ProjectionHandlerName).ConfigureAwait(false)).Should().Be(0);

                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

                var stillHeld = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, outboxOptions, messageId).ConfigureAwait(false);
                stillHeld.Should().NotBeNull();
                stillHeld!.Status.Should().Be(OutboxStatus.Pending);
                (await CountRunsAsync(_fixture.DataSource, tables, orderId, ProjectionHandlerName).ConfigureAwait(false)).Should().Be(0);

                (await WaitUntilAllPublishedAsync(_fixture.DataSource, outboxOptions, [messageId]).ConfigureAwait(false)).Should().BeTrue();

                var published = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, outboxOptions, messageId).ConfigureAwait(false);
                published.Should().NotBeNull();
                published!.Status.Should().Be(OutboxStatus.Published);
                published.AttemptCount.Should().Be(1);

                (await ReadProjectionExistsAsync(_fixture.DataSource, tables, orderId).ConfigureAwait(false)).Should().BeTrue();
                (await CountRunsAsync(_fixture.DataSource, tables, orderId, ProjectionHandlerName).ConfigureAwait(false)).Should().Be(1);
            }
            finally
            {
                await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    [SkippableFact]
    public async Task TwoHosts_WhenSendingConcurrently_ShouldDispatchEveryEnvelopeExactlyOnce()
    {
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        var tables = CreateTables(outboxOptions);
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, outboxOptions).ConfigureAwait(false);
        await EnsureDomainTablesAsync(_fixture.DataSource, tables).ConfigureAwait(false);

        var commandsA = new List<SubmitImmediateOrderCommand>(10);
        var commandsB = new List<SubmitImmediateOrderCommand>(10);

        for (var index = 0; index < 10; index++)
        {
            commandsA.Add(new SubmitImmediateOrderCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
            commandsB.Add(new SubmitImmediateOrderCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        }

        var providerA = BuildProvider(tables, outboxOptions, leaseOwner: "imm-host-a");
        await using (providerA.ConfigureAwait(false))
        {
            var providerB = BuildProvider(tables, outboxOptions, leaseOwner: "imm-host-b");
            await using (providerB.ConfigureAwait(false))
            {
                using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(providerA, runCts.Token).ConfigureAwait(false);
                await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(providerB, runCts.Token).ConfigureAwait(false);

                try
                {
                    var mediatorA = providerA.GetRequiredService<ICommandMediator>();
                    var mediatorB = providerB.GetRequiredService<ICommandMediator>();

                    await Task.WhenAll(
                        SendAllAsync(mediatorA, commandsA),
                        SendAllAsync(mediatorB, commandsB)).ConfigureAwait(false);

                    var allMessageIds = new List<Guid>(40);

                    foreach (var command in commandsA.Concat(commandsB))
                    {
                        allMessageIds.Add(command.MessageId);
                        allMessageIds.Add(command.AuditMessageId);
                    }

                    (await WaitUntilAllPublishedAsync(_fixture.DataSource, outboxOptions, allMessageIds).ConfigureAwait(false)).Should().BeTrue();

                    foreach (var command in commandsA.Concat(commandsB))
                    {
                        (await ReadProjectionExistsAsync(_fixture.DataSource, tables, command.OrderId).ConfigureAwait(false)).Should().BeTrue();

                        var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, outboxOptions, command.MessageId).ConfigureAwait(false);
                        row.Should().NotBeNull();
                        row!.Status.Should().Be(OutboxStatus.Published);
                        row.AttemptCount.Should().Be(1);

                        var auditRow = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, outboxOptions, command.AuditMessageId).ConfigureAwait(false);
                        auditRow.Should().NotBeNull();
                        auditRow!.Status.Should().Be(OutboxStatus.Published);
                        auditRow.AttemptCount.Should().Be(1);

                        (await CountRunsAsync(_fixture.DataSource, tables, command.OrderId, ProjectionHandlerName).ConfigureAwait(false)).Should().Be(1);
                        (await CountRunsAsync(_fixture.DataSource, tables, command.OrderId, AuditHandlerName).ConfigureAwait(false)).Should().Be(1);
                    }
                }
                finally
                {
                    await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(providerB, CancellationToken.None).ConfigureAwait(false);
                    await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(providerA, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        async Task SendAllAsync(ICommandMediator mediator, IReadOnlyList<SubmitImmediateOrderCommand> commands)
        {
            foreach (var command in commands)
            {
                var returned = await mediator.SendAsync(command).ConfigureAwait(false);
                returned.Should().Be(command.OrderId);
            }
        }
    }

    [SkippableFact]
    public async Task SendManyAsync_WhenBatchCommits_ShouldDispatchEveryCommandEnvelopeBeforeReturn()
    {
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        var tables = CreateTables(outboxOptions);
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, outboxOptions).ConfigureAwait(false);
        await EnsureDomainTablesAsync(_fixture.DataSource, tables).ConfigureAwait(false);

        var provider = BuildProvider(
            tables,
            outboxOptions,
            configureServices: services =>
            {
                services.AddSingleton<CommitNotificationCount>();
                services.AddScoped<IUnitOfWorkCommitListener, CommitCountingListener>();
            });
        await using (provider.ConfigureAwait(false))
        {
            using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);

            try
            {
                IReadOnlyList<ICommand> batch =
                [
                    new SubmitImmediateOrderCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
                    new SubmitImmediateOrderCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
                    new SubmitImmediateOrderCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())
                ];

                var batchMediator = provider.GetRequiredService<ICommandBatchMediator>();
                await batchMediator.SendManyAsync(batch).ConfigureAwait(false);

                provider.GetRequiredService<CommitNotificationCount>().Count.Should().Be(1);

                foreach (var command in batch.OfType<SubmitImmediateOrderCommand>())
                {
                    (await ReadProjectionExistsAsync(_fixture.DataSource, tables, command.OrderId).ConfigureAwait(false)).Should().BeTrue();

                    var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, outboxOptions, command.MessageId).ConfigureAwait(false);
                    row.Should().NotBeNull();
                    row!.Status.Should().Be(OutboxStatus.Published);
                    row.AttemptCount.Should().Be(1);

                    (await CountRunsAsync(_fixture.DataSource, tables, command.OrderId, ProjectionHandlerName).ConfigureAwait(false)).Should().Be(1);
                }

                var auditMessageIds = batch.OfType<SubmitImmediateOrderCommand>().Select(command => command.AuditMessageId).ToList();
                (await WaitUntilAllPublishedAsync(_fixture.DataSource, outboxOptions, auditMessageIds).ConfigureAwait(false)).Should().BeTrue();
            }
            finally
            {
                await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    [SkippableFact]
    public async Task SendManyAsync_WhenImmediateDispatchTimesOut_ShouldReleaseRemainingEnvelopesForProcessor()
    {
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        var tables = CreateTables(outboxOptions);
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, outboxOptions).ConfigureAwait(false);
        await EnsureDomainTablesAsync(_fixture.DataSource, tables).ConfigureAwait(false);

        var provider = BuildProvider(
            tables,
            outboxOptions,
            configureImmediateDispatch: options =>
            {
                options.Timeout = TimeSpan.FromSeconds(2);
            });
        await using (provider.ConfigureAwait(false))
        {
            IReadOnlyList<ICommand> batch =
            [
                new SubmitBlockingOrderCommand(Guid.NewGuid(), Guid.NewGuid()),
                new SubmitBlockingOrderCommand(Guid.NewGuid(), Guid.NewGuid())
            ];

            var batchMediator = provider.GetRequiredService<ICommandBatchMediator>();

            var beforeSend = DateTimeOffset.UtcNow;
            await batchMediator.SendManyAsync(batch).ConfigureAwait(false);
            var afterSend = DateTimeOffset.UtcNow;

            foreach (var command in batch.OfType<SubmitBlockingOrderCommand>())
            {
                var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, outboxOptions, command.MessageId).ConfigureAwait(false);
                row.Should().NotBeNull();
                row!.Status.Should().Be(OutboxStatus.Pending);
                row.AttemptCount.Should().Be(0);
                row.VisibleAfter.Should().NotBeNull();
                (row.VisibleAfter!.Value >= beforeSend && row.VisibleAfter.Value <= afterSend).Should().BeTrue();

                (await CountRunsAsync(_fixture.DataSource, tables, command.OrderId, BlockingHandlerName).ConfigureAwait(false)).Should().Be(0);
            }

            provider.GetRequiredService<BlockingDispatchGate>().Open();

            using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);

            try
            {
                var messageIds = batch.OfType<SubmitBlockingOrderCommand>().Select(command => command.MessageId).ToList();
                (await WaitUntilAllPublishedAsync(_fixture.DataSource, outboxOptions, messageIds).ConfigureAwait(false)).Should().BeTrue();

                foreach (var command in batch.OfType<SubmitBlockingOrderCommand>())
                {
                    (await ReadProjectionExistsAsync(_fixture.DataSource, tables, command.OrderId).ConfigureAwait(false)).Should().BeTrue();
                    (await CountRunsAsync(_fixture.DataSource, tables, command.OrderId, BlockingHandlerName).ConfigureAwait(false)).Should().Be(1);

                    var published = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, outboxOptions, command.MessageId).ConfigureAwait(false);
                    published.Should().NotBeNull();
                    published!.Status.Should().Be(OutboxStatus.Published);
                    published.AttemptCount.Should().Be(1);
                }
            }
            finally
            {
                await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private ServiceProvider BuildProvider(
        ImmediateDispatchTables tables,
        PostgreSqlOutboxStoreOptions outboxOptions,
        string leaseOwner = "imm-publisher",
        Action<OutboxImmediateDispatchOptions>? configureImmediateDispatch = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_fixture.DataSource);
        services.AddSingleton(tables);
        services.AddSingleton<BlockingDispatchGate>();
        services.AddScoped<IPostgreSqlTransactionProvider>(provider =>
            (IPostgreSqlTransactionProvider) provider.GetRequiredService<IUnitOfWork>());
        configureServices?.Invoke(services);

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands =>
            {
                commands.Register<SubmitImmediateOrderCommand>();
                commands.Register<SubmitImmediateOrderCommandHandler>();
                commands.Register<SubmitFlakyOrderCommand>();
                commands.Register<SubmitFlakyOrderCommandHandler>();
                commands.Register<SubmitBlockingOrderCommand>();
                commands.Register<SubmitBlockingOrderCommandHandler>();
                commands.UseUnitOfWork<ImmediateDispatchUnitOfWork>();
            });

            registry.AddEvents(events =>
            {
                events.Register<ImmediateOrderProjectionHandler>();
                events.Register<OrderAuditHandler>();
                events.Register<FlakyImmediateOrderProjectionHandler>();
                events.Register<BlockingImmediateOrderProjectionHandler>();
            });

            registry.AddOutbox(outbox =>
            {
                outbox.UsePostgreSqlStorage(postgres =>
                {
                    postgres.UseDataSource(_fixture.DataSource);
                    postgres.UseOptions(outboxOptions);
                    postgres.EnableAmbientTransactionProvider();
                });

                outbox.Contracts.Register<ImmediateOrderSubmittedEvent>("orders.events.immediate-submitted");
                outbox.Contracts.Register<OrderAuditEvent>("orders.events.audit");
                outbox.Contracts.Register<FlakyOrderSubmittedEvent>("orders.events.flaky-submitted");
                outbox.Contracts.Register<BlockingOrderSubmittedEvent>("orders.events.blocking-submitted");

                outbox.UseProcessorOptions(new OutboxProcessorOptions
                {
                    BatchSize = 20,
                    LeaseOwner = leaseOwner,
                    Retry = new RetryOptions
                    {
                        MaxAttempts = 5,
                        InitialDelay = TimeSpan.FromSeconds(1),
                        UseJitter = false
                    }
                });

                outbox.EnableOutboxProcessor(host =>
                {
                    host.PollInterval = TimeSpan.FromMilliseconds(100);
                });

                outbox.UseInProcessDispatch();
                outbox.EnableImmediateDispatch(configureImmediateDispatch);
            });
        });

        return services.BuildServiceProvider();
    }

    private static ImmediateDispatchTables CreateTables(PostgreSqlOutboxStoreOptions outboxOptions)
    {
        var suffix = Guid.NewGuid().ToString("N");

        return new ImmediateDispatchTables(
            $"orders_imm_{suffix}",
            $"projections_imm_{suffix}",
            $"runs_imm_{suffix}",
            outboxOptions.SchemaName,
            outboxOptions.TableName);
    }

    private static async Task EnsureDomainTablesAsync(NpgsqlDataSource dataSource, ImmediateDispatchTables tables)
    {
        var connection = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText =
                    $"""
                     CREATE TABLE IF NOT EXISTS "{PostgreSqlTestInfrastructure.TestSchemaName}"."{tables.OrdersTable}" (
                         order_id uuid NOT NULL PRIMARY KEY,
                         amount numeric NOT NULL);
                     CREATE TABLE IF NOT EXISTS "{PostgreSqlTestInfrastructure.TestSchemaName}"."{tables.ProjectionsTable}" (
                         order_id uuid NOT NULL PRIMARY KEY,
                         projected_at timestamptz NOT NULL);
                     CREATE TABLE IF NOT EXISTS "{PostgreSqlTestInfrastructure.TestSchemaName}"."{tables.RunsTable}" (
                         order_id uuid NOT NULL,
                         handler_name text NOT NULL,
                         succeeded boolean NOT NULL,
                         run_at timestamptz NOT NULL);
                     """;

                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task InsertOrderAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ImmediateDispatchTables tables,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                 INSERT INTO "{PostgreSqlTestInfrastructure.TestSchemaName}"."{tables.OrdersTable}" (order_id, amount)
                 VALUES (@order_id, @amount);
                 """;
            command.Parameters.AddWithValue("order_id", orderId);
            command.Parameters.AddWithValue("amount", OrderAmount);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertProjectionAsync(
        NpgsqlConnection connection,
        ImmediateDispatchTables tables,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText =
                $"""
                 INSERT INTO "{PostgreSqlTestInfrastructure.TestSchemaName}"."{tables.ProjectionsTable}" (order_id, projected_at)
                 VALUES (@order_id, @projected_at)
                 ON CONFLICT (order_id) DO NOTHING;
                 """;
            command.Parameters.AddWithValue("order_id", orderId);
            command.Parameters.AddWithValue("projected_at", DateTimeOffset.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertRunAsync(
        NpgsqlConnection connection,
        ImmediateDispatchTables tables,
        Guid orderId,
        string handlerName,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText =
                $"""
                 INSERT INTO "{PostgreSqlTestInfrastructure.TestSchemaName}"."{tables.RunsTable}" (order_id, handler_name, succeeded, run_at)
                 VALUES (@order_id, @handler_name, @succeeded, @run_at);
                 """;
            command.Parameters.AddWithValue("order_id", orderId);
            command.Parameters.AddWithValue("handler_name", handlerName);
            command.Parameters.AddWithValue("succeeded", succeeded);
            command.Parameters.AddWithValue("run_at", DateTimeOffset.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<int> CountRunsAsync(
        NpgsqlDataSource dataSource,
        ImmediateDispatchTables tables,
        Guid orderId,
        string handlerName)
    {
        var connection = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await CountRunsAsync(connection, tables, orderId, handlerName, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<int> CountRunsAsync(
        NpgsqlConnection connection,
        ImmediateDispatchTables tables,
        Guid orderId,
        string handlerName,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText =
                $"""
                 SELECT COUNT(*) FROM "{PostgreSqlTestInfrastructure.TestSchemaName}"."{tables.RunsTable}"
                 WHERE order_id = @order_id AND handler_name = @handler_name;
                 """;
            command.Parameters.AddWithValue("order_id", orderId);
            command.Parameters.AddWithValue("handler_name", handlerName);
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }
    }

    private static async Task<int> CountSucceededRunsAsync(
        NpgsqlDataSource dataSource,
        ImmediateDispatchTables tables,
        Guid orderId,
        string handlerName)
    {
        var connection = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText =
                    $"""
                     SELECT COUNT(*) FROM "{PostgreSqlTestInfrastructure.TestSchemaName}"."{tables.RunsTable}"
                     WHERE order_id = @order_id AND handler_name = @handler_name AND succeeded = @succeeded;
                     """;
                command.Parameters.AddWithValue("order_id", orderId);
                command.Parameters.AddWithValue("handler_name", handlerName);
                command.Parameters.AddWithValue("succeeded", true);
                var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
                return Convert.ToInt32(result, CultureInfo.InvariantCulture);
            }
        }
    }

    private static async Task<bool> ReadProjectionExistsAsync(
        NpgsqlDataSource dataSource,
        ImmediateDispatchTables tables,
        Guid orderId)
    {
        var connection = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText =
                    $"""
                     SELECT COUNT(*) FROM "{PostgreSqlTestInfrastructure.TestSchemaName}"."{tables.ProjectionsTable}"
                     WHERE order_id = @order_id;
                     """;
                command.Parameters.AddWithValue("order_id", orderId);
                var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
                return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
            }
        }
    }

    private static async Task<bool> WaitUntilAllPublishedAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlOutboxStoreOptions outboxOptions,
        IReadOnlyList<Guid> messageIds)
    {
        return await PostgreSqlTestInfrastructure.WaitUntilAsync(async () =>
        {
            foreach (var messageId in messageIds)
            {
                var row = await PostgreSqlTableReaders.ReadOutboxAsync(dataSource, outboxOptions, messageId).ConfigureAwait(false);

                if (row is null || row.Status != OutboxStatus.Published)
                {
                    return false;
                }
            }

            return true;
        }, DeliveryTimeout).ConfigureAwait(false);
    }

    private static async Task<List<Guid>> ListPendingMessageIdsAsync(
        NpgsqlDataSource dataSource,
        ImmediateDispatchTables tables,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText =
                    $"""
                     SELECT message_id FROM "{tables.OutboxSchema}"."{tables.OutboxTable}"
                     WHERE status = @pending_status;
                     """;
                command.Parameters.AddWithValue("pending_status", (int) OutboxStatus.Pending);

                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    List<Guid> messageIds = [];

                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        messageIds.Add(reader.GetGuid(0));
                    }

                    return messageIds;
                }
            }
        }
    }

    private static async Task ResetStagedEnvelopesToPendingAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlOutboxStoreOptions outboxOptions,
        IReadOnlyList<Guid> messageIds)
    {
        var connection = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            foreach (var messageId in messageIds)
            {
                var command = connection.CreateCommand();
                await using (command.ConfigureAwait(false))
                {
                    command.CommandText =
                        $"""
                         UPDATE "{outboxOptions.SchemaName}"."{outboxOptions.TableName}"
                         SET status = @pending_status,
                             attempt_count = 0,
                             lease_owner = NULL,
                             lease_expires_at = NULL
                         WHERE message_id = @message_id;
                         """;
                    command.Parameters.AddWithValue("pending_status", (int) OutboxStatus.Pending);
                    command.Parameters.AddWithValue("message_id", messageId);
                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private sealed record ImmediateDispatchTables(
        string OrdersTable,
        string ProjectionsTable,
        string RunsTable,
        string OutboxSchema,
        string OutboxTable);

    private sealed record SubmitImmediateOrderCommand(Guid OrderId, Guid MessageId, Guid AuditMessageId) : ICommand<Guid>;

    private sealed class SubmitImmediateOrderCommandHandler : ICommandHandler<SubmitImmediateOrderCommand, Guid>
    {
        private readonly ITransactionalOutbox _outbox;
        private readonly ImmediateDispatchTables _tables;
        private readonly IPostgreSqlTransactionProvider _transactions;

        public SubmitImmediateOrderCommandHandler(
            IPostgreSqlTransactionProvider transactions,
            ITransactionalOutbox outbox,
            ImmediateDispatchTables tables)
        {
            ArgumentNullException.ThrowIfNull(transactions);
            ArgumentNullException.ThrowIfNull(outbox);
            ArgumentNullException.ThrowIfNull(tables);

            _transactions = transactions;
            _outbox = outbox;
            _tables = tables;
        }

        public async Task<Guid> HandleAsync(SubmitImmediateOrderCommand message, CancellationToken cancellationToken = default)
        {
            if (!_transactions.TryGetCurrent(out var connection, out var transaction))
            {
                throw new InvalidOperationException("The unit of work did not open a PostgreSQL transaction for the command.");
            }

            await InsertOrderAsync(connection, transaction, _tables, message.OrderId, cancellationToken).ConfigureAwait(false);
            await _outbox.EnqueueAsync(
                OutboxEnqueueItem<ImmediateOrderSubmittedEvent>.WithIdentity(
                    new ImmediateOrderSubmittedEvent { OrderId = message.OrderId },
                    message.MessageId),
                cancellationToken).ConfigureAwait(false);
            await _outbox.EnqueueAsync(
                OutboxEnqueueItem<OrderAuditEvent>.WithIdentity(
                    new OrderAuditEvent { OrderId = message.OrderId },
                    message.AuditMessageId),
                cancellationToken).ConfigureAwait(false);

            return message.OrderId;
        }
    }

    private sealed record SubmitFlakyOrderCommand(Guid OrderId, Guid MessageId) : ICommand<Guid>;

    private sealed class SubmitFlakyOrderCommandHandler : ICommandHandler<SubmitFlakyOrderCommand, Guid>
    {
        private readonly ITransactionalOutbox _outbox;
        private readonly ImmediateDispatchTables _tables;
        private readonly IPostgreSqlTransactionProvider _transactions;

        public SubmitFlakyOrderCommandHandler(
            IPostgreSqlTransactionProvider transactions,
            ITransactionalOutbox outbox,
            ImmediateDispatchTables tables)
        {
            ArgumentNullException.ThrowIfNull(transactions);
            ArgumentNullException.ThrowIfNull(outbox);
            ArgumentNullException.ThrowIfNull(tables);

            _transactions = transactions;
            _outbox = outbox;
            _tables = tables;
        }

        public async Task<Guid> HandleAsync(SubmitFlakyOrderCommand message, CancellationToken cancellationToken = default)
        {
            if (!_transactions.TryGetCurrent(out var connection, out var transaction))
            {
                throw new InvalidOperationException("The unit of work did not open a PostgreSQL transaction for the command.");
            }

            await InsertOrderAsync(connection, transaction, _tables, message.OrderId, cancellationToken).ConfigureAwait(false);
            await _outbox.EnqueueAsync(
                OutboxEnqueueItem<FlakyOrderSubmittedEvent>.WithIdentity(
                    new FlakyOrderSubmittedEvent { OrderId = message.OrderId },
                    message.MessageId),
                cancellationToken).ConfigureAwait(false);

            return message.OrderId;
        }
    }

    private sealed record SubmitBlockingOrderCommand(Guid OrderId, Guid MessageId) : ICommand<Guid>;

    private sealed class SubmitBlockingOrderCommandHandler : ICommandHandler<SubmitBlockingOrderCommand, Guid>
    {
        private readonly ITransactionalOutbox _outbox;
        private readonly ImmediateDispatchTables _tables;
        private readonly IPostgreSqlTransactionProvider _transactions;

        public SubmitBlockingOrderCommandHandler(
            IPostgreSqlTransactionProvider transactions,
            ITransactionalOutbox outbox,
            ImmediateDispatchTables tables)
        {
            ArgumentNullException.ThrowIfNull(transactions);
            ArgumentNullException.ThrowIfNull(outbox);
            ArgumentNullException.ThrowIfNull(tables);

            _transactions = transactions;
            _outbox = outbox;
            _tables = tables;
        }

        public async Task<Guid> HandleAsync(SubmitBlockingOrderCommand message, CancellationToken cancellationToken = default)
        {
            if (!_transactions.TryGetCurrent(out var connection, out var transaction))
            {
                throw new InvalidOperationException("The unit of work did not open a PostgreSQL transaction for the command.");
            }

            await InsertOrderAsync(connection, transaction, _tables, message.OrderId, cancellationToken).ConfigureAwait(false);
            await _outbox.EnqueueAsync(
                OutboxEnqueueItem<BlockingOrderSubmittedEvent>.WithIdentity(
                    new BlockingOrderSubmittedEvent { OrderId = message.OrderId },
                    message.MessageId),
                cancellationToken).ConfigureAwait(false);

            return message.OrderId;
        }
    }

    [DispatchImmediately]
    private sealed record ImmediateOrderSubmittedEvent
    {
        public required Guid OrderId { get; init; }
    }

    private sealed class ImmediateOrderProjectionHandler : IEventHandler<ImmediateOrderSubmittedEvent>
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ImmediateDispatchTables _tables;

        public ImmediateOrderProjectionHandler(NpgsqlDataSource dataSource, ImmediateDispatchTables tables)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(tables);

            _dataSource = dataSource;
            _tables = tables;
        }

        public async Task HandleAsync(ImmediateOrderSubmittedEvent message, CancellationToken cancellationToken = default)
        {
            var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await InsertRunAsync(connection, _tables, message.OrderId, ProjectionHandlerName, true, cancellationToken).ConfigureAwait(false);
                await InsertProjectionAsync(connection, _tables, message.OrderId, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed record OrderAuditEvent
    {
        public required Guid OrderId { get; init; }
    }

    private sealed class OrderAuditHandler : IEventHandler<OrderAuditEvent>
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ImmediateDispatchTables _tables;

        public OrderAuditHandler(NpgsqlDataSource dataSource, ImmediateDispatchTables tables)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(tables);

            _dataSource = dataSource;
            _tables = tables;
        }

        public async Task HandleAsync(OrderAuditEvent message, CancellationToken cancellationToken = default)
        {
            var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await InsertRunAsync(connection, _tables, message.OrderId, AuditHandlerName, true, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    [DispatchImmediately]
    private sealed record FlakyOrderSubmittedEvent
    {
        public required Guid OrderId { get; init; }
    }

    private sealed class FlakyImmediateOrderProjectionHandler : IEventHandler<FlakyOrderSubmittedEvent>
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ImmediateDispatchTables _tables;

        public FlakyImmediateOrderProjectionHandler(NpgsqlDataSource dataSource, ImmediateDispatchTables tables)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(tables);

            _dataSource = dataSource;
            _tables = tables;
        }

        public async Task HandleAsync(FlakyOrderSubmittedEvent message, CancellationToken cancellationToken = default)
        {
            var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                var priorRuns = await CountRunsAsync(connection, _tables, message.OrderId, FlakyHandlerName, cancellationToken).ConfigureAwait(false);
                await InsertRunAsync(connection, _tables, message.OrderId, FlakyHandlerName, priorRuns > 0, cancellationToken).ConfigureAwait(false);

                if (priorRuns == 0)
                {
                    throw new InvalidOperationException("The flaky projection fails its first attempt so the outbox processor retries it.");
                }

                await InsertProjectionAsync(connection, _tables, message.OrderId, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    [DispatchImmediately]
    private sealed record BlockingOrderSubmittedEvent
    {
        public required Guid OrderId { get; init; }
    }

    private sealed class BlockingImmediateOrderProjectionHandler : IEventHandler<BlockingOrderSubmittedEvent>
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly BlockingDispatchGate _gate;
        private readonly ImmediateDispatchTables _tables;

        public BlockingImmediateOrderProjectionHandler(
            NpgsqlDataSource dataSource,
            BlockingDispatchGate gate,
            ImmediateDispatchTables tables)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(gate);
            ArgumentNullException.ThrowIfNull(tables);

            _dataSource = dataSource;
            _gate = gate;
            _tables = tables;
        }

        public async Task HandleAsync(BlockingOrderSubmittedEvent message, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await InsertRunAsync(connection, _tables, message.OrderId, BlockingHandlerName, true, cancellationToken).ConfigureAwait(false);
                await InsertProjectionAsync(connection, _tables, message.OrderId, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class BlockingDispatchGate
    {
        private readonly TaskCompletionSource<bool> _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitAsync(CancellationToken cancellationToken)
        {
            return _open.Task.WaitAsync(cancellationToken);
        }

        public void Open()
        {
            _ = _open.TrySetResult(true);
        }
    }

    private sealed class ImmediateDispatchUnitOfWork : IUnitOfWork, IUnitOfWorkBoundary, IPostgreSqlTransactionProvider, IAsyncDisposable
    {
        private readonly NpgsqlDataSource _dataSource;
        private NpgsqlConnection? _connection;
        private NpgsqlTransaction? _transaction;

        public ImmediateDispatchUnitOfWork(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);

            _dataSource = dataSource;
        }

        public async Task BeginAsync(CancellationToken cancellationToken = default)
        {
            _connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            _transaction = await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_transaction is null)
            {
                return;
            }

            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            await DisposeTransactionAsync().ConfigureAwait(false);
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (_transaction is null)
            {
                return;
            }

            await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            await DisposeTransactionAsync().ConfigureAwait(false);
        }

        public bool TryGetCurrent(
            [NotNullWhen(true)] out NpgsqlConnection? connection,
            [NotNullWhen(true)] out NpgsqlTransaction? transaction)
        {
            connection = _connection;
            transaction = _transaction;
            return connection is not null && transaction is not null;
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeTransactionAsync().ConfigureAwait(false);
        }

        private async Task DisposeTransactionAsync()
        {
            if (_transaction is not null)
            {
                await _transaction.DisposeAsync().ConfigureAwait(false);
                _transaction = null;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
            }
        }
    }

    private sealed class SimulatedCrashCommitListener : IUnitOfWorkCommitListener
    {
        public const string LeaseOwner = "simulated-crash";

        private static readonly TimeSpan LeaseDuration = TimeSpan.FromHours(1);

        private readonly NpgsqlDataSource _dataSource;
        private readonly IOutboxTargetedLeaseStore _leaseStore;
        private readonly ImmediateDispatchTables _tables;

        public SimulatedCrashCommitListener(
            NpgsqlDataSource dataSource,
            IOutboxTargetedLeaseStore leaseStore,
            ImmediateDispatchTables tables)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(leaseStore);
            ArgumentNullException.ThrowIfNull(tables);

            _dataSource = dataSource;
            _leaseStore = leaseStore;
            _tables = tables;
        }

        public async Task OnCommittedAsync(CancellationToken cancellationToken = default)
        {
            var messageIds = await ListPendingMessageIdsAsync(_dataSource, _tables, cancellationToken).ConfigureAwait(false);

            if (messageIds.Count > 0)
            {
                await _leaseStore.LeaseByIdAsync(
                    new OutboxTargetedLeaseRequest
                    {
                        MessageIds = messageIds,
                        LeaseOwner = LeaseOwner,
                        LeaseDuration = LeaseDuration,
                        Now = DateTimeOffset.UtcNow
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            throw new InvalidOperationException("Simulated crash before immediate dispatch.");
        }
    }

    private sealed class CommitNotificationCount
    {
        private int _count;

        public int Count => _count;

        public void Increment()
        {
            _ = Interlocked.Increment(ref _count);
        }
    }

    private sealed class CommitCountingListener : IUnitOfWorkCommitListener
    {
        private readonly CommitNotificationCount _count;

        public CommitCountingListener(CommitNotificationCount count)
        {
            ArgumentNullException.ThrowIfNull(count);

            _count = count;
        }

        public Task OnCommittedAsync(CancellationToken cancellationToken = default)
        {
            _count.Increment();
            return Task.CompletedTask;
        }
    }
}
