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
using IInboxProcessor = LiteBus.Inbox.Abstractions.IInboxProcessor;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

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
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, inboxOptions);

        var sagaOptions = new PostgreSqlSagaStoreOptions
        {
            SchemaName = PostgreSqlTestInfrastructure.TestSchemaName,
            TableName = $"saga_{Guid.NewGuid():N}"
        };

        await PostgreSqlSagaSchema.EnsureAsync(_fixture.DataSource, sagaOptions);

        await using var provider = BuildProvider(inboxOptions, sagaOptions);
        var inbox = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();
        var sagaStore = provider.GetRequiredService<ISagaStore>();

        await inbox.AcceptAsync(InboxAcceptItem<AdvanceOrderSagaCommand>.From(
            new AdvanceOrderSagaCommand(),
            InboxAcceptMetadata.Immediate with { Trace = new MessageTrace.Correlated("order-9001") }));

        await processor.ProcessPendingAsync();

        var instance = await sagaStore.LoadAsync<OrderSagaState>(
            new SagaCorrelation { CorrelationId = "order-9001", SagaDefinitionId = "orders.saga.advance" });

        instance.Should().NotBeNull();
        instance!.State.Step.Should().Be(1);
    }

    /// <summary>
    ///     Builds the service provider for saga inbox integration tests.
    /// </summary>
    /// <param name="InboxStoreOptions">The inbox PostgreSQL store options.</param>
    /// <param name="sagaOptions">The saga PostgreSQL store options.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(
        PostgreSqlInboxStoreOptions InboxStoreOptions,
        PostgreSqlSagaStoreOptions sagaOptions)
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddCommandModule(builder =>
            {
                builder.Register<AdvanceOrderSagaCommand>();
                builder.Register<AdvanceOrderSagaCommandHandler>();
            });

            registry.AddInboxModule(builder =>
            {
                builder.UsePostgreSqlStorage(postgres =>
                {
                    postgres.UseDataSource(_fixture.DataSource);
                    postgres.UseOptions(InboxStoreOptions);
                });

                builder.Contracts.Register<AdvanceOrderSagaCommand>("orders.saga.advance");

                builder.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = "pg-saga-e2e-worker"
                });

                builder.UseInProcessDispatch();
                builder.EnableSaga(registry => registry.MapState<OrderSagaState>("orders.saga.advance"));

                builder.UsePostgreSqlSagaStorage(postgres =>
                {
                    postgres.UseDataSource(_fixture.DataSource);
                    postgres.UseOptions(sagaOptions);
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