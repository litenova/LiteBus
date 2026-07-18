using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Saga.InboxIntegration;
using LiteBus.Saga.Abstractions;
using LiteBus.Saga.Storage.PostgreSql;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using IInboxProcessor = LiteBus.Inbox.Abstractions.IInboxProcessor;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     End-to-end tests for saga orchestration wired through <see cref="InboxModuleBuilderSagaExtensions.EnableSaga" />.
/// </summary>
public sealed class PostgreSqlSagaInboxEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlSagaInboxEndToEndTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL fixture.</param>
    public PostgreSqlSagaInboxEndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms saga state persists in PostgreSQL after inbox dispatch completes.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_with_EnableSaga_should_persist_state_in_postgresql()
    {
        var inboxOptions = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, inboxOptions).ConfigureAwait(false);

        var sagaOptions = new PostgreSqlSagaStoreOptions
        {
            SchemaName = PostgreSqlTestInfrastructure.TestSchemaName,
            TableName = $"saga_{Guid.NewGuid():N}"
        };

        await PostgreSqlSagaSchema.EnsureAsync(_fixture.DataSource, sagaOptions).ConfigureAwait(false);

        await using var provider = BuildProvider(inboxOptions, sagaOptions);
        var inbox = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();
        var sagaStore = provider.GetRequiredService<ISagaStore>();

        await inbox.AcceptAsync(InboxAcceptItem<AdvanceOrderSagaCommand>.From(
            new AdvanceOrderSagaCommand(),
            InboxAcceptMetadata.Immediate with { Trace = new MessageTrace.Correlated("order-9001") })).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var instance = await sagaStore.LoadAsync<OrderSagaState>(
            new SagaCorrelation { CorrelationId = "order-9001", SagaDefinitionId = "orders.saga.advance" }).ConfigureAwait(false);

        instance.Should().NotBeNull();
        instance!.State.Step.Should().Be(1);
    }

    /// <summary>
    ///     Confirms host startup creates the saga schema before the inbox processor begins polling.
    /// </summary>
    [Fact]
    public async Task HostedProcessor_StartAsync_ShouldInitializeSagaSchemaBeforeProcessing()
    {
        var inboxOptions = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        var sagaOptions = new PostgreSqlSagaStoreOptions
        {
            SchemaName = PostgreSqlTestInfrastructure.TestSchemaName,
            TableName = $"saga_{Guid.NewGuid():N}"
        };

        await using var provider = BuildProvider(inboxOptions, sagaOptions, enableProcessor: true);
        var hostedService = provider.GetRequiredService<IHostedService>();

        await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            await PostgreSqlSagaSchema.ValidateAsync(_fixture.DataSource, sagaOptions).ConfigureAwait(false);

            var inbox = provider.GetRequiredService<IInbox>();
            var sagaStore = provider.GetRequiredService<ISagaStore>();
            var correlation = new SagaCorrelation
            {
                CorrelationId = "order-hosted-startup",
                SagaDefinitionId = "orders.saga.advance"
            };

            await inbox.AcceptAsync(InboxAcceptItem<AdvanceOrderSagaCommand>.From(
                new AdvanceOrderSagaCommand(),
                InboxAcceptMetadata.Immediate with
                {
                    Trace = new MessageTrace.Correlated(correlation.CorrelationId)
                })).ConfigureAwait(false);

            var processed = await PostgreSqlTestInfrastructure.WaitUntilAsync(
                async () => (await sagaStore.LoadAsync<OrderSagaState>(correlation).ConfigureAwait(false)) is not null,
                TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            processed.Should().BeTrue();
            var instance = await sagaStore.LoadAsync<OrderSagaState>(correlation).ConfigureAwait(false);
            instance!.State.Step.Should().Be(1);
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Confirms tenant metadata from inbox accepts persists separate saga rows for one correlation identifier.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_WithTenantScopedAccepts_ShouldPersistSeparateSagaRows()
    {
        var inboxOptions = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, inboxOptions).ConfigureAwait(false);

        var sagaOptions = new PostgreSqlSagaStoreOptions
        {
            SchemaName = PostgreSqlTestInfrastructure.TestSchemaName,
            TableName = $"saga_{Guid.NewGuid():N}"
        };

        await PostgreSqlSagaSchema.EnsureAsync(_fixture.DataSource, sagaOptions).ConfigureAwait(false);

        await using var provider = BuildProvider(inboxOptions, sagaOptions);
        var inbox = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();
        var sagaStore = provider.GetRequiredService<ISagaStore>();

        await inbox.AcceptAsync(InboxAcceptItem<AdvanceOrderSagaCommand>.From(
            new AdvanceOrderSagaCommand(),
            InboxAcceptMetadata.Immediate with
            {
                Trace = new MessageTrace.Correlated("order-tenant-shared"),
                Tenant = new TenantScope.Isolated("tenant-a")
            })).ConfigureAwait(false);
        await inbox.AcceptAsync(InboxAcceptItem<AdvanceOrderSagaCommand>.From(
            new AdvanceOrderSagaCommand(),
            InboxAcceptMetadata.Immediate with
            {
                Trace = new MessageTrace.Correlated("order-tenant-shared"),
                Tenant = new TenantScope.Isolated("tenant-b")
            })).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var tenantA = await sagaStore.LoadAsync<OrderSagaState>(new SagaCorrelation
        {
            CorrelationId = "order-tenant-shared",
            SagaDefinitionId = "orders.saga.advance",
            TenantId = "tenant-a"
        }).ConfigureAwait(false);
        var tenantB = await sagaStore.LoadAsync<OrderSagaState>(new SagaCorrelation
        {
            CorrelationId = "order-tenant-shared",
            SagaDefinitionId = "orders.saga.advance",
            TenantId = "tenant-b"
        }).ConfigureAwait(false);
        var summaries = await sagaStore.QueryAsync(new SagaQueryFilter
        {
            CorrelationId = "order-tenant-shared"
        }).ConfigureAwait(false);

        tenantA!.State.Step.Should().Be(1);
        tenantB!.State.Step.Should().Be(1);
        summaries.Select(summary => summary.Correlation.TenantId)
            .Should().BeEquivalentTo(["tenant-a", "tenant-b"]);
    }

    /// <summary>
    ///     Builds the service provider for saga inbox integration tests.
    /// </summary>
    /// <param name="inboxStoreOptions">The inbox PostgreSQL store options.</param>
    /// <param name="sagaOptions">The saga PostgreSQL store options.</param>
    /// <param name="enableProcessor">Whether to start the hosted inbox processor.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(
        PostgreSqlInboxStoreOptions inboxStoreOptions,
        PostgreSqlSagaStoreOptions sagaOptions,
        bool enableProcessor = false)
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(builder =>
            {
                builder.Register<AdvanceOrderSagaCommand>();
                builder.Register<AdvanceOrderSagaCommandHandler>();
            });

            registry.AddInbox(builder =>
            {
                builder.UsePostgreSqlStorage(postgres =>
                {
                    postgres.UseDataSource(_fixture.DataSource);
                    postgres.UseOptions(inboxStoreOptions);
                });

                builder.Contracts.Register<AdvanceOrderSagaCommand>("orders.saga.advance");

                builder.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = "pg-saga-e2e-worker"
                });

                builder.UseInProcessDispatch();

                if (enableProcessor)
                {
                    builder.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromMilliseconds(25));
                }

                builder.EnableSaga(saga =>
                {
                    saga.MapState<OrderSagaState>("orders.saga.advance");
                    saga.UsePostgreSqlStorage(postgres =>
                    {
                        postgres.UseDataSource(_fixture.DataSource);
                        postgres.UseOptions(sagaOptions);
                    });
                });
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Saga state tracked for one order workflow.
    /// </summary>
    private sealed class OrderSagaState
    {
        /// <summary>
        ///     Gets or sets the current step.
        /// </summary>
        public int Step { get; set; }
    }

    /// <summary>
    ///     Command that participates in the order saga.
    /// </summary>
    private sealed class AdvanceOrderSagaCommand : ICommand;

    /// <summary>
    ///     Handler that advances saga state through <see cref="ISagaContext" />.
    /// </summary>
    private sealed class AdvanceOrderSagaCommandHandler : ICommandHandler<AdvanceOrderSagaCommand>
    {
        private readonly ISagaContext _sagaContext;

        /// <summary>
        ///     Initializes a new instance of the <see cref="AdvanceOrderSagaCommandHandler" /> class.
        /// </summary>
        /// <param name="sagaContext">The ambient saga context.</param>
        public AdvanceOrderSagaCommandHandler(ISagaContext sagaContext)
        {
            _sagaContext = sagaContext;
        }

        /// <inheritdoc />
        public Task HandleAsync(AdvanceOrderSagaCommand command, CancellationToken cancellationToken = default)
        {
            if (_sagaContext.IsActive)
            {
                var state = _sagaContext.GetState<OrderSagaState>();
                state.Step++;
                _sagaContext.SetState(state);
            }

            return Task.CompletedTask;
        }
    }
}
