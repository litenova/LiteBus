using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Saga.Abstractions;

namespace LiteBus.Storage.UnitTests.Saga;

/// <summary>
///     Verifies saga reads are pure: observing state never marks the scope changed, while in-place mutations are
///     detected through the serialized snapshot.
/// </summary>
public sealed class SagaPureReadTests
{
    /// <summary>
    ///     Verifies reading state and then completing completes the saga row.
    /// </summary>
    [Fact]
    public async Task AfterDispatchAsync_WhenStateIsReadThenCompleted_CompletesSaga()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var sagaStore = new InMemorySagaStore(serializer);
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<PureReadSagaState>("process-order");
        var correlation = new SagaCorrelation
        {
            CorrelationId = "order-42",
            SagaDefinitionId = "process-order"
        };
        await sagaStore.SaveAsync(SagaSaveItem<PureReadSagaState>.From(
            correlation,
            new PureReadSagaState { Step = 3 },
            0)).ConfigureAwait(false);

        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(sagaStore, registry, serializer, context);
        var envelope = new PureReadEnvelope("process-order", "order-42");

        await hook.BeforeDispatchAsync(envelope).ConfigureAwait(false);
        hook.PrepareDispatchScope(envelope);
        context.GetState<PureReadSagaState>().Step.Should().Be(3);
        context.Complete();
        await hook.AfterDispatchAsync(envelope).ConfigureAwait(false);

        var completed = await sagaStore.LoadAsync<PureReadSagaState>(correlation).ConfigureAwait(false);
        completed.Should().NotBeNull();
        completed!.IsCompleted.Should().BeTrue();
        completed.Version.Should().Be(2);
        completed.State.Step.Should().Be(3);
        context.IsActive.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies a dispatch that only reads state performs no save and creates no row.
    /// </summary>
    [Fact]
    public async Task AfterDispatchAsync_WhenDispatchOnlyReadsState_SkipsSave()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var sagaStore = new CountingSagaStore(new InMemorySagaStore(serializer));
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<PureReadSagaState>("process-order");
        var correlation = new SagaCorrelation
        {
            CorrelationId = "order-43",
            SagaDefinitionId = "process-order"
        };
        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(sagaStore, registry, serializer, context);
        var envelope = new PureReadEnvelope("process-order", "order-43");

        await hook.BeforeDispatchAsync(envelope).ConfigureAwait(false);
        hook.PrepareDispatchScope(envelope);
        context.GetState<PureReadSagaState>().Step.Should().Be(0);
        await hook.AfterDispatchAsync(envelope).ConfigureAwait(false);

        sagaStore.SaveCount.Should().Be(0);
        (await sagaStore.LoadAsync<PureReadSagaState>(correlation).ConfigureAwait(false)).Should().BeNull();
    }

    /// <summary>
    ///     Verifies an in-place mutation without <c>SetState</c> is detected and persisted.
    /// </summary>
    [Fact]
    public async Task AfterDispatchAsync_WhenStateChangesInPlace_PersistsWithoutSetState()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var sagaStore = new InMemorySagaStore(serializer);
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<PureReadSagaState>("process-order");
        var correlation = new SagaCorrelation
        {
            CorrelationId = "order-44",
            SagaDefinitionId = "process-order"
        };
        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(sagaStore, registry, serializer, context);
        var envelope = new PureReadEnvelope("process-order", "order-44");

        await hook.BeforeDispatchAsync(envelope).ConfigureAwait(false);
        hook.PrepareDispatchScope(envelope);
        context.GetState<PureReadSagaState>().Step = 7;
        await hook.AfterDispatchAsync(envelope).ConfigureAwait(false);

        var loaded = await sagaStore.LoadAsync<PureReadSagaState>(correlation).ConfigureAwait(false);
        loaded.Should().NotBeNull();
        loaded!.State.Step.Should().Be(7);
        loaded.Version.Should().Be(1);
        loaded.LastAppliedMessageId.Should().Be(envelope.MessageId);
    }

    /// <summary>
    ///     Verifies an in-place mutation combined with completion is rejected.
    /// </summary>
    [Fact]
    public async Task AfterDispatchAsync_WhenInPlaceChangeCombinesWithComplete_ThrowsInvalidOperationException()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var sagaStore = new InMemorySagaStore(serializer);
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<PureReadSagaState>("process-order");
        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(sagaStore, registry, serializer, context);
        var envelope = new PureReadEnvelope("process-order", "order-45");

        await hook.BeforeDispatchAsync(envelope).ConfigureAwait(false);
        hook.PrepareDispatchScope(envelope);
        context.GetState<PureReadSagaState>().Step = 7;
        context.Complete();

        var action = () => hook.AfterDispatchAsync(envelope);

        await action.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Saga state tracked for pure-read tests.
    /// </summary>
    private sealed class PureReadSagaState
    {
        /// <summary>
        ///     Gets or sets the current step.
        /// </summary>
        public int Step { get; set; }
    }

    /// <summary>
    ///     Minimal processor envelope for pure-read tests.
    /// </summary>
    private sealed class PureReadEnvelope : IProcessorEnvelope
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="PureReadEnvelope" /> class.
        /// </summary>
        /// <param name="contractName">The contract name.</param>
        /// <param name="correlationId">The optional correlation identifier.</param>
        public PureReadEnvelope(string contractName, string? correlationId)
        {
            ContractName = contractName;
            CorrelationId = correlationId;
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
        public string? TenantId => null;
    }

    /// <summary>
    ///     Saga store decorator that counts save calls.
    /// </summary>
    private sealed class CountingSagaStore : ISagaStore
    {
        /// <summary>
        ///     The wrapped saga store.
        /// </summary>
        private readonly ISagaStore _inner;

        /// <summary>
        ///     Initializes a new instance of the <see cref="CountingSagaStore" /> class.
        /// </summary>
        /// <param name="inner">The wrapped saga store.</param>
        public CountingSagaStore(ISagaStore inner)
        {
            _inner = inner;
        }

        /// <summary>
        ///     Gets the number of save calls observed.
        /// </summary>
        public int SaveCount { get; private set; }

        /// <inheritdoc />
        public Task<SagaInstance<TState>?> LoadAsync<TState>(
            SagaCorrelation correlation,
            CancellationToken cancellationToken = default)
            where TState : class, new()
        {
            return _inner.LoadAsync<TState>(correlation, cancellationToken);
        }

        /// <inheritdoc />
        public Task SaveAsync<TState>(
            SagaSaveItem<TState> item,
            CancellationToken cancellationToken = default)
            where TState : class, new()
        {
            SaveCount++;
            return _inner.SaveAsync(item, cancellationToken);
        }

        /// <inheritdoc />
        public Task CompleteAsync(SagaCompleteItem item, CancellationToken cancellationToken = default)
        {
            return _inner.CompleteAsync(item, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<SagaInstanceSummary>> QueryAsync(
            SagaQueryFilter filter,
            CancellationToken cancellationToken = default)
        {
            return _inner.QueryAsync(filter, cancellationToken);
        }

        /// <inheritdoc />
        public Task<int> PurgeAsync(SagaPurgeFilter filter, CancellationToken cancellationToken = default)
        {
            return _inner.PurgeAsync(filter, cancellationToken);
        }
    }
}
