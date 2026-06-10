using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Saga;
using LiteBus.Saga.Abstractions;
using LiteBus.Saga.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     Shared saga orchestration composition and handlers for PostgreSQL integration tests.
/// </summary>
internal static class SagaOrchestrationTestSupport
{
    /// <summary>
    ///     The contract name shared by all workflow commands in the order saga tests.
    /// </summary>
    internal const string WorkflowContractName = "orders.saga.order-workflow";

    /// <summary>
    ///     Creates isolated PostgreSQL saga store options for one test run.
    /// </summary>
    /// <returns>The saga store options.</returns>
    internal static PostgreSqlSagaStoreOptions CreateSagaOptions()
    {
        return new PostgreSqlSagaStoreOptions
        {
            SchemaName = PostgreSqlTestInfrastructure.TestSchemaName,
            TableName = $"saga_{Guid.NewGuid():N}"
        };
    }

    /// <summary>
    ///     Builds a service provider with inbox, saga orchestration, and PostgreSQL persistence configured.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    /// <param name="inboxOptions">The inbox store options.</param>
    /// <param name="sagaOptions">The saga store options.</param>
    /// <param name="configureProcessor">An optional callback that tunes inbox processor options.</param>
    /// <returns>The configured service provider.</returns>
    internal static ServiceProvider BuildSagaProvider(
        PostgreSqlFixture fixture,
        PostgreSqlInboxStoreOptions inboxOptions,
        PostgreSqlSagaStoreOptions sagaOptions,
        Func<InboxProcessorOptions, InboxProcessorOptions>? configureProcessor = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<SagaStepFailureGate>();
        services.AddSingleton<SagaConcurrencyDelayGate>();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ => { });
            registry.AddCommandModule(builder =>
            {
                builder.Register<OrderWorkflowSagaCommand>();
                builder.Register<OrderWorkflowSagaCommandHandler>();
            });

            registry.AddInboxModule(builder =>
            {
                builder.UsePostgreSqlStorage(postgres =>
                {
                    postgres.UseDataSource(fixture.DataSource);
                    postgres.UseOptions(inboxOptions);
                });

                builder.Contracts.Register<OrderWorkflowSagaCommand>(WorkflowContractName, 1);

                var processorOptions = new InboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = "pg-saga-depth-worker"
                };

                processorOptions = configureProcessor?.Invoke(processorOptions) ?? processorOptions;
                builder.UseProcessorOptions(processorOptions);
                builder.UseCommandInboxDispatcher();
                builder.EnableSaga(registry => registry.MapState<OrderWorkflowSagaState>(WorkflowContractName));
                builder.UsePostgreSqlSagaStorage(postgres =>
                {
                    postgres.UseDataSource(fixture.DataSource);
                    postgres.UseOptions(sagaOptions);
                });
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Resolves the saga correlation used by workflow tests.
    /// </summary>
    /// <param name="correlationId">The business correlation identifier.</param>
    /// <returns>The saga correlation.</returns>
    internal static SagaCorrelation CreateCorrelation(string correlationId)
    {
        return new SagaCorrelation
        {
            CorrelationId = correlationId,
            SagaType = WorkflowContractName
        };
    }

    /// <summary>
    ///     Mutable saga state for the order workflow integration tests.
    /// </summary>
    internal sealed class OrderWorkflowSagaState
    {
        /// <summary>
        ///     Gets or sets the current workflow step.
        /// </summary>
        public int Step { get; set; }

        /// <summary>
        ///     Gets or sets a value indicating whether inventory was reserved.
        /// </summary>
        public bool InventoryReserved { get; set; }

        /// <summary>
        ///     Gets or sets a value indicating whether payment was captured.
        /// </summary>
        public bool PaymentCaptured { get; set; }

        /// <summary>
        ///     Gets or sets a value indicating whether compensation ran.
        /// </summary>
        public bool Compensated { get; set; }
    }

    /// <summary>
    ///     Workflow step identifiers used by <see cref="OrderWorkflowSagaCommand" />.
    /// </summary>
    internal enum OrderWorkflowStep
    {
        /// <summary>
        ///     Reserves inventory for the order.
        /// </summary>
        ReserveInventory = 1,

        /// <summary>
        ///     Captures payment for the order.
        /// </summary>
        CapturePayment = 2,

        /// <summary>
        ///     Compensates prior workflow steps after a failure.
        /// </summary>
        Compensate = 3,

        /// <summary>
        ///     Increments the workflow step without side effects for concurrency tests.
        /// </summary>
        Increment = 4
    }

    /// <summary>
    ///     Command that drives one step of the order workflow saga.
    /// </summary>
    internal sealed class OrderWorkflowSagaCommand : ICommand
    {
        /// <summary>
        ///     Gets the workflow step to execute.
        /// </summary>
        public OrderWorkflowStep Step { get; init; }
    }

    /// <summary>
    ///     Controls artificial handler delay used to widen concurrent saga update races.
    /// </summary>
    internal sealed class SagaConcurrencyDelayGate
    {
        /// <summary>
        ///     Gets or sets the delay applied before increment steps complete.
        /// </summary>
        public TimeSpan IncrementDelay { get; set; }
    }

    /// <summary>
    ///     Controls which workflow step should throw during handler execution.
    /// </summary>
    internal sealed class SagaStepFailureGate
    {
        /// <summary>
        ///     The workflow step that should fail, or <see langword="null" /> when handlers should succeed.
        /// </summary>
        private OrderWorkflowStep? _failingStep;

        /// <summary>
        ///     Configures the workflow step that should throw on the next matching dispatch.
        /// </summary>
        /// <param name="step">The workflow step that should fail.</param>
        public void FailOn(OrderWorkflowStep step)
        {
            _failingStep = step;
        }

        /// <summary>
        ///     Clears any configured failure step.
        /// </summary>
        public void Clear()
        {
            _failingStep = null;
        }

        /// <summary>
        ///     Gets a value indicating whether the supplied step should throw.
        /// </summary>
        /// <param name="step">The workflow step being executed.</param>
        /// <returns><see langword="true" /> when the step should throw; otherwise <see langword="false" />.</returns>
        public bool ShouldFail(OrderWorkflowStep step)
        {
            return _failingStep == step;
        }
    }

    /// <summary>
    ///     Handler that mutates order workflow saga state through <see cref="ISagaContext" />.
    /// </summary>
    internal sealed class OrderWorkflowSagaCommandHandler : ICommandHandler<OrderWorkflowSagaCommand>
    {
        private readonly ISagaContext _sagaContext;
        private readonly SagaStepFailureGate _failureGate;
        private readonly SagaConcurrencyDelayGate _delayGate;

        /// <summary>
        ///     Initializes a new instance of the <see cref="OrderWorkflowSagaCommandHandler" /> class.
        /// </summary>
        /// <param name="sagaContext">The ambient saga context.</param>
        /// <param name="failureGate">The gate that controls simulated handler failures.</param>
        /// <param name="delayGate">The gate that controls artificial handler delay.</param>
        public OrderWorkflowSagaCommandHandler(
            ISagaContext sagaContext,
            SagaStepFailureGate failureGate,
            SagaConcurrencyDelayGate delayGate)
        {
            _sagaContext = sagaContext;
            _failureGate = failureGate;
            _delayGate = delayGate;
        }

        /// <inheritdoc />
        public async Task HandleAsync(OrderWorkflowSagaCommand command, CancellationToken cancellationToken = default)
        {
            if (_failureGate.ShouldFail(command.Step))
            {
                throw new InvalidOperationException($"Simulated failure on workflow step '{command.Step}'.");
            }

            if (!_sagaContext.IsActive)
            {
                return;
            }

            if (command.Step == OrderWorkflowStep.Increment && _delayGate.IncrementDelay > TimeSpan.Zero)
            {
                await Task.Delay(_delayGate.IncrementDelay, cancellationToken).ConfigureAwait(false);
            }

            var state = _sagaContext.GetState<OrderWorkflowSagaState>();

            switch (command.Step)
            {
                case OrderWorkflowStep.ReserveInventory:
                    state.InventoryReserved = true;
                    state.Step = 1;
                    break;

                case OrderWorkflowStep.CapturePayment:
                    if (!state.InventoryReserved)
                    {
                        throw new InvalidOperationException("Cannot capture payment before inventory is reserved.");
                    }

                    state.PaymentCaptured = true;
                    state.Step = 2;
                    break;

                case OrderWorkflowStep.Compensate:
                    state.InventoryReserved = false;
                    state.PaymentCaptured = false;
                    state.Compensated = true;
                    break;

                case OrderWorkflowStep.Increment:
                    state.Step++;
                    break;
            }

            _sagaContext.SetState(state);
        }
    }
}
