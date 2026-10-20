using LiteBus.Events.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Abstractions.Exceptions;
using LiteBus.Outbox.Dispatch.InProcess;

namespace LiteBus.Outbox.UnitTests.Dispatch.InProcess;

/// <summary>
///     Verifies that <see cref="EventOutboxDispatcher" /> lets caller cancellation escape unwrapped
///     and only adds registration guidance to registration failures.
/// </summary>
public sealed class EventOutboxDispatcherCancellationTests
{
    private const string ContractName = "tests.events.cancellation";

    /// <summary>
    ///     Verifies a publish that observes caller cancellation throws <see cref="OperationCanceledException" /> unwrapped.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_when_publish_observes_caller_cancellation_should_throw_unwrapped()
    {
        using var cancellation = new CancellationTokenSource();
        var mediator = new CancellingEventMediator(cancellation);
        var contracts = new MessageContractRegistry();
        contracts.Register<CancellationTestEvent>(ContractName);
        var serializer = new SystemTextJsonMessageSerializer();
        var dispatcher = new EventOutboxDispatcher(mediator, contracts, serializer);
        var envelope = await CreateEnvelopeAsync(serializer).ConfigureAwait(false);

        var act = () => dispatcher.DispatchAsync(envelope, cancellation.Token);

        var failure = await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
        failure.Which.Should().BeOfType<OperationCanceledException>();
        failure.Which.Should().NotBeOfType<LiteBusDispatchException>();
        mediator.PublishCount.Should().Be(1);
    }

    /// <summary>
    ///     Verifies a handler failure is wrapped without registration guidance, which would point at a configuration
    ///     bug that does not exist.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_when_handler_fails_should_wrap_without_registration_guidance()
    {
        var mediator = new ThrowingEventMediator(new InvalidOperationException("handler failed"));
        var contracts = new MessageContractRegistry();
        contracts.Register<CancellationTestEvent>(ContractName);
        var serializer = new SystemTextJsonMessageSerializer();
        var dispatcher = new EventOutboxDispatcher(mediator, contracts, serializer);
        var envelope = await CreateEnvelopeAsync(serializer).ConfigureAwait(false);

        var act = () => dispatcher.DispatchAsync(envelope);

        var failure = await act.Should().ThrowAsync<LiteBusDispatchException>().ConfigureAwait(false);
        failure.Which.InnerException.Should().BeOfType<InvalidOperationException>();
        failure.Which.Message.Should().NotContain("Contracts.Register");
    }

    /// <summary>
    ///     Verifies an unregistered contract is wrapped with registration guidance.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_when_contract_is_unregistered_should_wrap_with_registration_guidance()
    {
        var dispatcher = new EventOutboxDispatcher(
            new ThrowingEventMediator(new InvalidOperationException("must not run")),
            new MessageContractRegistry(),
            new SystemTextJsonMessageSerializer());

        var act = () => dispatcher.DispatchAsync(new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.events.unknown",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Publishing,
            AttemptCount = 1
        });

        var failure = await act.Should().ThrowAsync<LiteBusDispatchException>().ConfigureAwait(false);
        failure.Which.InnerException.Should().BeOfType<MessageContractNotRegisteredException>();
        failure.Which.Message.Should().Contain("Contracts.Register");
    }

    private static async Task<OutboxEnvelope> CreateEnvelopeAsync(SystemTextJsonMessageSerializer serializer)
    {
        var payload = await serializer.SerializeAsync(new CancellationTestEvent("order-1")).ConfigureAwait(false);

        return new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = ContractName,
            ContractVersion = 1,
            Payload = payload,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Publishing,
            AttemptCount = 1
        };
    }

    private sealed record CancellationTestEvent(string OrderId) : IEvent;

    private sealed class CancellingEventMediator : IEventMediator
    {
        private readonly CancellationTokenSource _cancellation;

        public CancellingEventMediator(CancellationTokenSource cancellation)
        {
            ArgumentNullException.ThrowIfNull(cancellation);
            _cancellation = cancellation;
        }

        public int PublishCount { get; private set; }

        public Task PublishAsync(
            IEvent @event,
            EventMediationSettings? eventMediationSettings = null,
            CancellationToken cancellationToken = default)
        {
            PublishCount++;
            _cancellation.Cancel();
            throw new OperationCanceledException(_cancellation.Token);
        }

        public Task PublishAsync<TEvent>(
            TEvent @event,
            EventMediationSettings? eventMediationSettings = null,
            CancellationToken cancellationToken = default)
            where TEvent : notnull
        {
            PublishCount++;
            _cancellation.Cancel();
            throw new OperationCanceledException(_cancellation.Token);
        }
    }

    private sealed class ThrowingEventMediator : IEventMediator
    {
        private readonly Exception _failure;

        public ThrowingEventMediator(Exception failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            _failure = failure;
        }

        public Task PublishAsync(
            IEvent @event,
            EventMediationSettings? eventMediationSettings = null,
            CancellationToken cancellationToken = default)
        {
            throw _failure;
        }

        public Task PublishAsync<TEvent>(
            TEvent @event,
            EventMediationSettings? eventMediationSettings = null,
            CancellationToken cancellationToken = default)
            where TEvent : notnull
        {
            throw _failure;
        }
    }
}
