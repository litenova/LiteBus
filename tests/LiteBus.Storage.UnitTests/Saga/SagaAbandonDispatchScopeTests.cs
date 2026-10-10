using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Saga.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.UnitTests.Saga;

/// <summary>
///     Verifies abandoned saga dispatches release their state without throwing, and that one failing abandon hook
///     still lets later hooks run.
/// </summary>
public sealed class SagaAbandonDispatchScopeTests
{
    /// <summary>
    ///     Verifies abandoning a dispatch with an empty message identifier does not throw.
    /// </summary>
    [Fact]
    public void AbandonDispatchScope_WithEmptyMessageId_DoesNotThrow()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<AbandonSagaState>("process-order");
        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(new InMemorySagaStore(serializer), registry, serializer, context);

        var action = () => hook.AbandonDispatchScope(new AbandonEnvelope("process-order", "order-42", Guid.Empty));

        action.Should().NotThrow();
        context.IsActive.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies a throwing abandon hook does not skip later hooks and the saga correlation gate is released.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_WhenFirstAbandonHookThrows_LaterHooksStillRun()
    {
        var store = new InMemoryInboxStore();
        var serializer = new SystemTextJsonMessageSerializer();
        var sagaStore = new InMemorySagaStore(serializer);
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<AbandonSagaState>("process-order");
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<AbandonOrderCommand>("process-order");
        var context = new SagaExecutionContext();
        var sagaHook = new SagaProcessorHook(sagaStore, registry, serializer, context);
        var recordingHook = new RecordingAbandonHook();

        var services = new ServiceCollection();
        services.AddSingleton<IInboxStore>(store);
        services.AddSingleton<IInboxProcessingStore>(store);
        services.AddSingleton<IInboxLeaseStore>(store);
        services.AddSingleton<IInboxStateWriter>(store);
        services.AddSingleton<ISagaStore>(sagaStore);
        services.AddSingleton<ISagaStateTypeRegistry>(registry);
        services.AddSingleton(context);
        services.AddSingleton<ISagaContext>(sp => sp.GetRequiredService<SagaExecutionContext>());
        services.AddSingleton<IProcessorEnvelopeHook>(new ThrowingAbandonHook());
        services.AddSingleton<IProcessorEnvelopeHook>(recordingHook);
        services.AddSingleton<IProcessorEnvelopeHook>(sagaHook);
        services.AddSingleton<IContractReader>(contractRegistry);
        services.AddSingleton<IMessageSerializer>(serializer);
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IInboxDispatcher, ThrowingSagaDispatcher>();
        var options = new InboxProcessorOptions { BatchSize = 10, DispatcherConcurrency = 1 };
        services.AddSingleton(options);

        services.AddSingleton<IInboxProcessor>(sp => new PipelinedInboxProcessor(
            sp.GetRequiredService<IInboxLeaseStore>(),
            sp.GetRequiredService<IInboxStateWriter>(),
            sp.GetRequiredService<IInboxDispatcher>(),
            options,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetServices<IProcessorEnvelopeHook>().ToArray()));

        await using var provider = services.BuildServiceProvider();
        var inbox = new Inbox.Inbox(
            store,
            new InboxEnvelopeFactory(
                provider.GetRequiredService<IContractReader>(),
                provider.GetRequiredService<IMessageSerializer>(),
                TimeProvider.System));

        await inbox.AcceptAsync(InboxAcceptItem<AbandonOrderCommand>.From(
            new AbandonOrderCommand(),
            InboxAcceptMetadata.Immediate with { Trace = new MessageTrace.Correlated("order-42") })).ConfigureAwait(false);

        InvalidOperationException? thrown = null;

        try
        {
            await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            thrown = exception;
        }

        thrown.Should().NotBeNull();
        thrown!.Message.Should().Be("Abandon failed.");
        recordingHook.AbandonCount.Should().Be(1);

        using var gateProbe = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var probe = new AbandonEnvelope("process-order", "order-42");
        await sagaHook.BeforeDispatchAsync(probe, gateProbe.Token).ConfigureAwait(false);
        sagaHook.AbandonDispatchScope(probe);
    }

    /// <summary>
    ///     Saga state tracked for abandon dispatch tests.
    /// </summary>
    private sealed class AbandonSagaState
    {
        /// <summary>
        ///     Gets or sets the current step.
        /// </summary>
        public int Step { get; set; }
    }

    /// <summary>
    ///     Command that participates in the abandon saga test.
    /// </summary>
    private sealed class AbandonOrderCommand : ICommand;

    /// <summary>
    ///     Minimal processor envelope with an optional fixed message identifier for abandon tests.
    /// </summary>
    private sealed class AbandonEnvelope : IProcessorEnvelope
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="AbandonEnvelope" /> class.
        /// </summary>
        /// <param name="contractName">The contract name.</param>
        /// <param name="correlationId">The optional correlation identifier.</param>
        /// <param name="messageId">The optional fixed message identifier.</param>
        public AbandonEnvelope(string contractName, string? correlationId, Guid? messageId = null)
        {
            ContractName = contractName;
            CorrelationId = correlationId;
            MessageId = messageId ?? Guid.NewGuid();
        }

        /// <inheritdoc />
        public Guid MessageId { get; }

        /// <inheritdoc />
        public string ContractName { get; }

        /// <inheritdoc />
        public int ContractVersion { get; } = 1;

        /// <inheritdoc />
        public string? CorrelationId { get; }

        /// <inheritdoc />
        public string? CausationId => null;

        /// <inheritdoc />
        public string? TenantId => null;
    }

    /// <summary>
    ///     Dispatcher that fails every inbox dispatch.
    /// </summary>
    private sealed class ThrowingSagaDispatcher : IInboxDispatcher
    {
        /// <inheritdoc />
        public Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Dispatch failed.");
        }
    }

    /// <summary>
    ///     Hook whose abandon path always throws.
    /// </summary>
    private sealed class ThrowingAbandonHook : IProcessorEnvelopeHook
    {
        /// <inheritdoc />
        public Task BeforeDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void AbandonDispatchScope(IProcessorEnvelope envelope)
        {
            throw new InvalidOperationException("Abandon failed.");
        }

        /// <inheritdoc />
        public Task AfterDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Hook that counts abandon calls.
    /// </summary>
    private sealed class RecordingAbandonHook : IProcessorEnvelopeHook
    {
        /// <summary>
        ///     Gets the number of abandon calls observed.
        /// </summary>
        public int AbandonCount { get; private set; }

        /// <inheritdoc />
        public Task BeforeDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void AbandonDispatchScope(IProcessorEnvelope envelope)
        {
            AbandonCount++;
        }

        /// <inheritdoc />
        public Task AfterDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
