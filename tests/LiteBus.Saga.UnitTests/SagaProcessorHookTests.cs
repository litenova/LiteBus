using AwesomeAssertions;
using LiteBus.Commands;
using Xunit;
using LiteBus.Commands.Abstractions;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Orchestration.Abstractions;
using LiteBus.Saga;
using LiteBus.Saga.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Saga.UnitTests;

/// <summary>
///     Verifies saga state is loaded and persisted around inbox dispatch.
/// </summary>
public sealed class SagaProcessorHookTests
{
    /// <summary>
    ///     Verifies saga state increments when a correlated command is processed.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_PersistsSagaStateAfterSuccessfulDispatch()
    {
        var store = new InMemoryInboxStore();
        var serializer = new SystemTextJsonMessageSerializer();
        var sagaStore = new InMemorySagaStore(serializer);
        var registry = new SagaStateTypeRegistry();
        registry.Register<OrderSagaState>("process-order");
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<ProcessOrderCommand>("process-order");

        var services = new ServiceCollection();
        services.AddSingleton<IInboxStore>(store);
        services.AddSingleton<IInboxProcessingStore>(store);
        services.AddSingleton<IInboxLeaseStore>(store);
        services.AddSingleton<IInboxStateWriter>(store);
        services.AddSingleton<ISagaStore>(sagaStore);
        services.AddSingleton<ISagaStateTypeRegistry>(registry);
        services.AddSingleton(new SagaExecutionContext());
        services.AddSingleton<ISagaContext>(sp => sp.GetRequiredService<SagaExecutionContext>());
        services.AddSingleton<IProcessorEnvelopeHook, SagaProcessorHook>();
        services.AddSingleton<IContractReader>(contractRegistry);
        services.AddSingleton<IMessageSerializer>(serializer);
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IInboxDispatcher, SagaMutatingDispatcher>();
        var options = new InboxProcessorOptions { BatchSize = 10, DispatcherConcurrency = 1 };
        services.AddSingleton(options);
        services.AddSingleton<IInboxProcessor>(sp => new PipelinedInboxProcessor(
            sp.GetRequiredService<IInboxLeaseStore>(),
            sp.GetRequiredService<IInboxStateWriter>(),
            sp.GetRequiredService<IInboxDispatcher>(),
            options,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetServices<IProcessorEnvelopeHook>().ToArray()));

        var provider = services.BuildServiceProvider();
        var inbox = new global::LiteBus.Inbox.Inbox(
            store,
            new InboxEnvelopeFactory(
                provider.GetRequiredService<IContractReader>(),
                provider.GetRequiredService<IMessageSerializer>(),
                TimeProvider.System),
            TimeProvider.System);
        await inbox.AcceptAsync(
            new ProcessOrderCommand(),
            new InboxOptions { CorrelationId = "order-42" });

        var processor = provider.GetRequiredService<IInboxProcessor>();
        await processor.ProcessPendingAsync();

        var instance = await sagaStore.LoadAsync<OrderSagaState>(
            new SagaCorrelation { CorrelationId = "order-42", SagaType = "process-order" });

        instance.Should().NotBeNull();
        instance!.State.Step.Should().Be(1);
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
    private sealed class ProcessOrderCommand : ICommand;

    /// <summary>
    ///     Test dispatcher that mutates saga state through <see cref="ISagaContext" />.
    /// </summary>
    private sealed class SagaMutatingDispatcher : IInboxDispatcher
    {
        /// <summary>
        ///     Gets the ambient saga context.
        /// </summary>
        private readonly ISagaContext _sagaContext;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SagaMutatingDispatcher" /> class.
        /// </summary>
        /// <param name="sagaContext">The ambient saga context.</param>
        public SagaMutatingDispatcher(ISagaContext sagaContext)
        {
            _sagaContext = sagaContext;
        }

        /// <inheritdoc />
        public Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
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
