using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Saga.Abstractions;

namespace LiteBus.Storage.UnitTests.Saga;

/// <summary>
///     Verifies a save without an applied message identifier preserves the duplicate-dispatch marker.
/// </summary>
public sealed class SagaDedupMarkerTests
{
    /// <summary>
    ///     Verifies a save without an identifier keeps the marker, so the marked message stays suppressed.
    /// </summary>
    [Fact]
    public async Task BeforeDispatchAsync_AfterSaveWithoutId_PreservesMarkerAndSuppressesRedelivery()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var sagaStore = new InMemorySagaStore(serializer);
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<DedupSagaState>("process-order");
        var correlation = new SagaCorrelation
        {
            CorrelationId = "order-42",
            SagaDefinitionId = "process-order"
        };
        var firstMessageId = Guid.NewGuid();

        await sagaStore.SaveAsync(SagaSaveItem<DedupSagaState>.From(
            correlation,
            new DedupSagaState { Step = 1 },
            0,
            firstMessageId)).ConfigureAwait(false);
        await sagaStore.SaveAsync(SagaSaveItem<DedupSagaState>.From(
            correlation,
            new DedupSagaState { Step = 2 },
            1)).ConfigureAwait(false);

        var loaded = await sagaStore.LoadAsync<DedupSagaState>(correlation).ConfigureAwait(false);
        loaded.Should().NotBeNull();
        loaded!.State.Step.Should().Be(2);
        loaded.Version.Should().Be(2);
        loaded.LastAppliedMessageId.Should().Be(firstMessageId);

        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(sagaStore, registry, serializer, context);
        var redelivery = new DedupEnvelope("process-order", "order-42", firstMessageId);

        await hook.BeforeDispatchAsync(redelivery).ConfigureAwait(false);
        hook.PrepareDispatchScope(redelivery);

        context.IsActive.Should().BeFalse();
        hook.ShouldDispatch(redelivery).Should().BeFalse();
    }

    /// <summary>
    ///     Saga state tracked for dedup marker tests.
    /// </summary>
    private sealed class DedupSagaState
    {
        /// <summary>
        ///     Gets or sets the current step.
        /// </summary>
        public int Step { get; set; }
    }

    /// <summary>
    ///     Minimal processor envelope with a fixed message identifier for dedup marker tests.
    /// </summary>
    private sealed class DedupEnvelope : IProcessorEnvelope
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="DedupEnvelope" /> class.
        /// </summary>
        /// <param name="contractName">The contract name.</param>
        /// <param name="correlationId">The optional correlation identifier.</param>
        /// <param name="messageId">The fixed message identifier.</param>
        public DedupEnvelope(string contractName, string? correlationId, Guid messageId)
        {
            ContractName = contractName;
            CorrelationId = correlationId;
            MessageId = messageId;
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
}
