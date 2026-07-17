using AwesomeAssertions;
using LiteBus.Commands.Abstractions;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.DurableMessaging.Abstractions.Processing;
using LiteBus.Saga;
using LiteBus.Saga.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.UnitTests.Saga;

/// <summary>
///     Saga state tracked for one order workflow in hook tests.
/// </summary>
public sealed class OrderSagaState
{
    /// <summary>
    ///     Gets or sets the current step.
    /// </summary>
    public int Step { get; set; }
}

/// <summary>
///     Verifies saga state is loaded and persisted around inbox dispatch.
/// </summary>
public sealed class SagaProcessorHookTests
{
    /// <summary>
    ///     Verifies registry resolution for the process-order contract used in hook tests.
    /// </summary>
    [Fact]
    public void StateTypeRegistry_ShouldResolveProcessOrderContract()
    {
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<OrderSagaState>("process-order");

        registry.ResolveDefinitionId("process-order").Should().Be("process-order");
        registry.ResolveStateType("process-order").Should().Be(typeof(OrderSagaState));
    }

    /// <summary>
    ///     Verifies messages without a registered saga definition do not activate a saga scope.
    /// </summary>
    [Fact]
    public async Task BeforeDispatchAsync_WhenContractIsNotRegistered_LeavesContextInactive()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(
            new InMemorySagaStore(serializer),
            new SagaStateTypeRegistry(),
            serializer,
            context);

        await hook.BeforeDispatchAsync(new TestProcessorEnvelope("unmapped", "order-42")).ConfigureAwait(false);

        context.IsActive.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies messages without correlation do not activate a mapped saga definition.
    /// </summary>
    [Fact]
    public async Task BeforeDispatchAsync_WhenCorrelationIsMissing_LeavesContextInactive()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<OrderSagaState>("process-order");
        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(new InMemorySagaStore(serializer), registry, serializer, context);

        await hook.BeforeDispatchAsync(new TestProcessorEnvelope("process-order", null)).ConfigureAwait(false);

        context.IsActive.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies tenant metadata participates in the active saga correlation.
    /// </summary>
    [Fact]
    public async Task BeforeDispatchAsync_WithTenant_AttachesTenantScopedCorrelation()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<OrderSagaState>("process-order");
        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(new InMemorySagaStore(serializer), registry, serializer, context);
        var envelope = new TestProcessorEnvelope("process-order", "order-42", "tenant-a");

        await hook.BeforeDispatchAsync(envelope).ConfigureAwait(false);
        hook.PrepareDispatchScope(envelope);

        context.Correlation.Should().Be(new SagaCorrelation
        {
            CorrelationId = "order-42",
            SagaDefinitionId = "process-order",
            TenantId = "tenant-a"
        });

        hook.AbandonDispatchScope(envelope);
    }

    /// <summary>
    ///     Verifies a completed saga does not expose state to another correlated dispatch.
    /// </summary>
    [Fact]
    public async Task BeforeDispatchAsync_WhenSagaIsCompleted_LeavesContextInactive()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var store = new InMemorySagaStore(serializer);
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<OrderSagaState>("process-order");
        var correlation = new SagaCorrelation
        {
            CorrelationId = "order-42",
            SagaDefinitionId = "process-order"
        };

        await store.SaveAsync(new SagaSaveItem<OrderSagaState>(
            correlation,
            new OrderSagaState { Step = 1 },
            0)).ConfigureAwait(false);
        await store.CompleteAsync(SagaCompleteItem.From(correlation, 1)).ConfigureAwait(false);

        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(store, registry, serializer, context);

        await hook.BeforeDispatchAsync(new TestProcessorEnvelope("process-order", "order-42"))
            .ConfigureAwait(false);

        context.IsActive.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies dirty state and completion in the same dispatch is rejected.
    /// </summary>
    [Fact]
    public async Task AfterDispatch_WhenDirtyAndComplete_ThrowsInvalidOperationException()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var sagaStore = new InMemorySagaStore(serializer);
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<OrderSagaState>("process-order");
        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(sagaStore, registry, serializer, context);

        var envelope = new TestProcessorEnvelope("process-order", "order-42");

        await hook.BeforeDispatchAsync(envelope).ConfigureAwait(true);
        hook.PrepareDispatchScope(envelope);

        context.IsActive.Should().BeTrue("BeforeDispatch should activate saga scope for correlated process-order messages");

        context.SetState(new OrderSagaState { Step = 1 });
        context.Complete();

        var action = async () => await hook.AfterDispatchAsync(envelope).ConfigureAwait(true);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    ///     Verifies parallel messages for one correlation retain separate ambient state snapshots.
    /// </summary>
    [Fact]
    public async Task ParallelDispatches_WithSameCorrelation_IsolateScopeByMessageId()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var sagaStore = new InMemorySagaStore(serializer);
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<OrderSagaState>("process-order");
        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(sagaStore, registry, serializer, context);
        async Task DispatchAsync(TestProcessorEnvelope envelope, int step)
        {
            await hook.BeforeDispatchAsync(envelope).ConfigureAwait(false);
            hook.PrepareDispatchScope(envelope);
            context.SetState(new OrderSagaState { Step = step });
            context.GetState<OrderSagaState>().Step.Should().Be(step);
            await hook.AfterDispatchAsync(envelope).ConfigureAwait(false);
        }

        var first = DispatchAsync(new TestProcessorEnvelope("process-order", "order-42"), 1);
        var second = DispatchAsync(new TestProcessorEnvelope("process-order", "order-42"), 2);

        await Task.WhenAll(first, second).ConfigureAwait(false);
        new[] { first, second }.Should().OnlyContain(task => task.IsCompletedSuccessfully);

        var persisted = await sagaStore.LoadAsync<OrderSagaState>(new SagaCorrelation
        {
            CorrelationId = "order-42",
            SagaDefinitionId = "process-order"
        }).ConfigureAwait(false);

        persisted.Should().NotBeNull();
        persisted!.Version.Should().Be(2);
        persisted.State.Step.Should().BeOneOf(1, 2);
    }

    /// <summary>
    ///     Verifies abandoning a failed dispatch removes its keyed scope before the envelope is retried.
    /// </summary>
    [Fact]
    public async Task AbandonDispatchScope_AllowsSameMessageToBeginAgain()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var sagaStore = new InMemorySagaStore(serializer);
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<OrderSagaState>("process-order");
        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(sagaStore, registry, serializer, context);
        var envelope = new TestProcessorEnvelope("process-order", "order-42");

        await hook.BeforeDispatchAsync(envelope).ConfigureAwait(false);
        hook.PrepareDispatchScope(envelope);
        context.IsActive.Should().BeTrue();

        hook.AbandonDispatchScope(envelope);
        context.IsActive.Should().BeFalse();

        await hook.BeforeDispatchAsync(envelope).ConfigureAwait(false);
        hook.PrepareDispatchScope(envelope);
        context.IsActive.Should().BeTrue();

        hook.AbandonDispatchScope(envelope);
    }

    /// <summary>
    ///     Verifies completion-only dispatch marks an existing saga row completed.
    /// </summary>
    [Fact]
    public async Task AfterDispatchAsync_WhenCompletionIsRequested_CompletesExistingSaga()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var sagaStore = new InMemorySagaStore(serializer);
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<OrderSagaState>("process-order");
        var correlation = new SagaCorrelation
        {
            CorrelationId = "order-42",
            SagaDefinitionId = "process-order"
        };
        await sagaStore.SaveAsync(new SagaSaveItem<OrderSagaState>(
            correlation,
            new OrderSagaState { Step = 3 },
            0)).ConfigureAwait(false);

        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(sagaStore, registry, serializer, context);
        var envelope = new TestProcessorEnvelope("process-order", "order-42");

        await hook.BeforeDispatchAsync(envelope).ConfigureAwait(false);
        hook.PrepareDispatchScope(envelope);
        context.Complete();
        await hook.AfterDispatchAsync(envelope).ConfigureAwait(false);

        var completed = await sagaStore.LoadAsync<OrderSagaState>(correlation).ConfigureAwait(false);
        completed.Should().NotBeNull();
        completed!.IsCompleted.Should().BeTrue();
        completed.Version.Should().Be(2);
        completed.State.Step.Should().Be(3);
        context.IsActive.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies completion-only conflicts reload the current active version before retrying.
    /// </summary>
    [Fact]
    public async Task AfterDispatchAsync_WhenCompletionConflictsWithActiveSaga_RetriesCurrentVersion()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var sagaStore = new CompletionConflictSagaStore(alwaysConflict: false);
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<OrderSagaState>("process-order");
        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(sagaStore, registry, serializer, context);
        var envelope = new TestProcessorEnvelope("process-order", "order-42");

        await hook.BeforeDispatchAsync(envelope).ConfigureAwait(false);
        hook.PrepareDispatchScope(envelope);
        context.Complete();

        await hook.AfterDispatchAsync(envelope).ConfigureAwait(false);

        sagaStore.LoadCount.Should().Be(2);
        sagaStore.CompleteCount.Should().Be(2);
        sagaStore.CompletedVersion.Should().Be(3);
        context.IsActive.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies completion-only retry remains bounded when every current-version write conflicts.
    /// </summary>
    [Fact]
    public async Task AfterDispatchAsync_WhenEveryCompletionAttemptConflicts_ThrowsAfterThreeAttempts()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var sagaStore = new CompletionConflictSagaStore(alwaysConflict: true);
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<OrderSagaState>("process-order");
        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(sagaStore, registry, serializer, context);
        var envelope = new TestProcessorEnvelope("process-order", "order-42");

        await hook.BeforeDispatchAsync(envelope).ConfigureAwait(false);
        hook.PrepareDispatchScope(envelope);
        context.Complete();

        var action = () => hook.AfterDispatchAsync(envelope);

        await action.Should().ThrowAsync<SagaConcurrencyException>().ConfigureAwait(false);
        sagaStore.LoadCount.Should().Be(3);
        sagaStore.CompleteCount.Should().Be(3);
        sagaStore.CompletedVersion.Should().BeNull();
        context.IsActive.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies a dirty state conflict propagates without retrying an unsafe stale snapshot.
    /// </summary>
    [Fact]
    public async Task AfterDispatchAsync_WhenDirtySaveConflicts_PropagatesAfterSingleAttempt()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var sagaStore = new AlwaysConflictingSagaStore();
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<OrderSagaState>("process-order");
        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(sagaStore, registry, serializer, context);
        var envelope = new TestProcessorEnvelope("process-order", "order-42");

        await hook.BeforeDispatchAsync(envelope).ConfigureAwait(false);
        hook.PrepareDispatchScope(envelope);
        context.SetState(new OrderSagaState { Step = 1 });

        var action = () => hook.AfterDispatchAsync(envelope);

        await action.Should().ThrowAsync<SagaConcurrencyException>().ConfigureAwait(false);
        sagaStore.SaveCount.Should().Be(1);
        sagaStore.LoadCount.Should().Be(1);
        context.IsActive.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that replaying the same durable message skips a second saga mutation.
    /// </summary>
    [Fact]
    public async Task BeforeDispatchAsync_WhenMessageWasAlreadyApplied_ShouldSkipSagaScope()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var sagaStore = new InMemorySagaStore(serializer);
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<OrderSagaState>("process-order");
        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(sagaStore, registry, serializer, context);
        var envelope = new TestProcessorEnvelope("process-order", "order-42");

        await hook.BeforeDispatchAsync(envelope).ConfigureAwait(false);
        hook.PrepareDispatchScope(envelope);
        context.SetState(new OrderSagaState { Step = 1 });
        await hook.AfterDispatchAsync(envelope).ConfigureAwait(false);

        await hook.BeforeDispatchAsync(envelope).ConfigureAwait(false);

        context.IsActive.Should().BeFalse();
        var persisted = await sagaStore.LoadAsync<OrderSagaState>(new SagaCorrelation
        {
            CorrelationId = "order-42",
            SagaDefinitionId = "process-order"
        }).ConfigureAwait(false);

        persisted.Should().NotBeNull();
        persisted!.State.Step.Should().Be(1);
        persisted.LastAppliedMessageId.Should().Be(envelope.MessageId);
    }

    /// <summary>
    ///     Verifies a dirty state conflict does not reload and overwrite state advanced by another worker.
    /// </summary>
    [Fact]
    public async Task AfterDispatchAsync_WhenConcurrentWorkerAdvancedSaga_DoesNotReloadDirtyState()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var sagaStore = new CompletingOnConflictSagaStore();
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<OrderSagaState>("process-order");
        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(sagaStore, registry, serializer, context);
        var envelope = new TestProcessorEnvelope("process-order", "order-42");

        await hook.BeforeDispatchAsync(envelope).ConfigureAwait(false);
        hook.PrepareDispatchScope(envelope);
        context.SetState(new OrderSagaState { Step = 2 });

        var action = () => hook.AfterDispatchAsync(envelope);

        await action.Should().ThrowAsync<SagaConcurrencyException>().ConfigureAwait(false);
        sagaStore.LoadCount.Should().Be(1);
        sagaStore.SaveCount.Should().Be(1);
        context.IsActive.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies state operations reject calls outside a processor-owned saga scope.
    /// </summary>
    [Fact]
    public void SagaExecutionContext_WhenInactive_RejectsStateOperations()
    {
        var context = new SagaExecutionContext();

        var get = () => context.GetState<OrderSagaState>();
        var set = () => context.SetState(new OrderSagaState());
        var complete = context.Complete;

        context.IsActive.Should().BeFalse();
        context.Correlation.Should().BeNull();
        get.Should().Throw<InvalidOperationException>();
        set.Should().Throw<InvalidOperationException>();
        complete.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    ///     Verifies concurrent inbox dispatches that request completion converge on one completed saga version.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_ConcurrentCompletionRequests_ShouldCompleteSagaOnce()
    {
        var store = new InMemoryInboxStore();
        var serializer = new SystemTextJsonMessageSerializer();
        var sagaStore = new InMemorySagaStore(serializer);
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<OrderSagaState>("process-order");
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<ProcessOrderCommand>("process-order");
        var correlation = new SagaCorrelation
        {
            CorrelationId = "order-42",
            SagaDefinitionId = "process-order"
        };

        await sagaStore.SaveAsync(SagaSaveItem<OrderSagaState>.From(
            correlation,
            new OrderSagaState { Step = 3 },
            0)).ConfigureAwait(false);

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
        services.AddSingleton<IInboxDispatcher, ConcurrentSagaCompletingDispatcher>();
        var options = new InboxProcessorOptions { BatchSize = 10, DispatcherConcurrency = 2 };
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

        await inbox.AcceptAsync(InboxAcceptItem<ProcessOrderCommand>.From(
            new ProcessOrderCommand(),
            InboxAcceptMetadata.Immediate with { Trace = new MessageTrace.Correlated("order-42") })).ConfigureAwait(false);
        await inbox.AcceptAsync(InboxAcceptItem<ProcessOrderCommand>.From(
            new ProcessOrderCommand(),
            InboxAcceptMetadata.Immediate with { Trace = new MessageTrace.Correlated("order-42") })).ConfigureAwait(false);

        await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync().ConfigureAwait(false);

        var completed = await sagaStore.LoadAsync<OrderSagaState>(correlation).ConfigureAwait(false);
        completed.Should().NotBeNull();
        completed!.IsCompleted.Should().BeTrue();
        completed.Version.Should().Be(2);
        store.GetAll(InboxStatus.Completed).Should().HaveCount(2);
    }

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
        registry.RegisterStateType<OrderSagaState>("process-order");
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

        var inbox = new Inbox.Inbox(
            store,
            new InboxEnvelopeFactory(
                provider.GetRequiredService<IContractReader>(),
                provider.GetRequiredService<IMessageSerializer>(),
                TimeProvider.System));

        await inbox.AcceptAsync(InboxAcceptItem<ProcessOrderCommand>.From(
            new ProcessOrderCommand(),
            InboxAcceptMetadata.Immediate with { Trace = new MessageTrace.Correlated("order-42") })).ConfigureAwait(true);


        var processor = provider.GetRequiredService<IInboxProcessor>();
        await processor.ProcessPendingAsync().ConfigureAwait(true);

        var instance = await sagaStore.LoadAsync<OrderSagaState>(
            new SagaCorrelation { CorrelationId = "order-42", SagaDefinitionId = "process-order" });

        instance.Should().NotBeNull();
        instance!.State.Step.Should().Be(1);
    }

    /// <summary>
    ///     Minimal processor envelope for hook tests.
    /// </summary>
    private sealed class TestProcessorEnvelope : IProcessorEnvelope
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="TestProcessorEnvelope" /> class.
        /// </summary>
        /// <param name="contractName">The contract name.</param>
        /// <param name="correlationId">The optional correlation identifier.</param>
        /// <param name="tenantId">The optional tenant identifier.</param>
        public TestProcessorEnvelope(string contractName, string? correlationId, string? tenantId = null)
        {
            ContractName = contractName;
            CorrelationId = correlationId;
            TenantId = tenantId;
        }

        /// <inheritdoc />
        public Guid MessageId { get; } = Guid.NewGuid();

        /// <inheritdoc />
        public string ContractName { get; }

        /// <inheritdoc />
        public int ContractVersion { get; } = 1;

        /// <inheritdoc />
        public string? CorrelationId { get; }

        /// <inheritdoc />
        public string? CausationId => null;

        /// <inheritdoc />
        public string? TenantId { get; }
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

    /// <summary>
    ///     Test dispatcher that requests saga completion.
    /// </summary>
    private sealed class ConcurrentSagaCompletingDispatcher : IInboxDispatcher
    {
        /// <summary>
        ///     Gets the ambient saga context.
        /// </summary>
        private readonly ISagaContext _sagaContext;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ConcurrentSagaCompletingDispatcher" /> class.
        /// </summary>
        /// <param name="sagaContext">The ambient saga context.</param>
        public ConcurrentSagaCompletingDispatcher(ISagaContext sagaContext)
        {
            _sagaContext = sagaContext;
        }

        /// <inheritdoc />
        public async Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (_sagaContext.IsActive)
            {
                _sagaContext.Complete();
            }
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }

    private sealed class AlwaysConflictingSagaStore : ISagaStore
    {
        internal int LoadCount { get; private set; }

        internal int SaveCount { get; private set; }

        public Task<SagaInstance<TState>?> LoadAsync<TState>(
            SagaCorrelation correlation,
            CancellationToken cancellationToken = default)
            where TState : class, new()
        {
            LoadCount++;
            return Task.FromResult<SagaInstance<TState>?>(null);
        }

        public async Task SaveAsync<TState>(
            SagaSaveItem<TState> item,
            CancellationToken cancellationToken = default)
            where TState : class, new()
        {
            SaveCount++;
            await Task.Yield();
            throw new SagaConcurrencyException(item.Correlation);
        }

        public Task CompleteAsync(SagaCompleteItem item, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<SagaInstanceSummary>> QueryAsync(
            SagaQueryFilter filter,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SagaInstanceSummary>>([]);
        }

        public Task<int> PurgeAsync(SagaPurgeFilter filter, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }

    private sealed class CompletingOnConflictSagaStore : ISagaStore
    {
        internal int LoadCount { get; private set; }

        internal int SaveCount { get; private set; }

        public Task<SagaInstance<TState>?> LoadAsync<TState>(
            SagaCorrelation correlation,
            CancellationToken cancellationToken = default)
            where TState : class, new()
        {
            LoadCount++;

            return Task.FromResult<SagaInstance<TState>?>(new SagaInstance<TState>
            {
                Correlation = correlation,
                State = new TState(),
                Version = LoadCount,
                IsCompleted = LoadCount > 1
            });
        }

        public async Task SaveAsync<TState>(
            SagaSaveItem<TState> item,
            CancellationToken cancellationToken = default)
            where TState : class, new()
        {
            SaveCount++;
            await Task.Yield();
            throw new SagaConcurrencyException(item.Correlation);
        }

        public Task CompleteAsync(SagaCompleteItem item, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<SagaInstanceSummary>> QueryAsync(
            SagaQueryFilter filter,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SagaInstanceSummary>>([]);
        }

        public Task<int> PurgeAsync(SagaPurgeFilter filter, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }

    private sealed class CompletionConflictSagaStore : ISagaStore
    {
        private readonly bool _alwaysConflict;

        internal CompletionConflictSagaStore(bool alwaysConflict)
        {
            _alwaysConflict = alwaysConflict;
        }

        internal int CompleteCount { get; private set; }

        internal int? CompletedVersion { get; private set; }

        internal int LoadCount { get; private set; }

        public Task<SagaInstance<TState>?> LoadAsync<TState>(
            SagaCorrelation correlation,
            CancellationToken cancellationToken = default)
            where TState : class, new()
        {
            LoadCount++;

            return Task.FromResult<SagaInstance<TState>?>(new SagaInstance<TState>
            {
                Correlation = correlation,
                State = new TState(),
                Version = LoadCount,
                IsCompleted = false
            });
        }

        public Task SaveAsync<TState>(
            SagaSaveItem<TState> item,
            CancellationToken cancellationToken = default)
            where TState : class, new()
        {
            throw new NotSupportedException();
        }

        public Task CompleteAsync(SagaCompleteItem item, CancellationToken cancellationToken = default)
        {
            CompleteCount++;

            if (_alwaysConflict || CompleteCount == 1)
            {
                throw new SagaConcurrencyException(item.Correlation);
            }

            CompletedVersion = item.ExpectedVersion + 1;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SagaInstanceSummary>> QueryAsync(
            SagaQueryFilter filter,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SagaInstanceSummary>>([]);
        }

        public Task<int> PurgeAsync(SagaPurgeFilter filter, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }
}
