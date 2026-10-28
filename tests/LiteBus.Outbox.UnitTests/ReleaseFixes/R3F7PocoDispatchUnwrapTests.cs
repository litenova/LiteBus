using LiteBus.Events.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Abstractions.Exceptions;
using LiteBus.Outbox.Dispatch.InProcess;

namespace LiteBus.Outbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies POCO dispatch surfaces handler failures unwrapped with registration guidance.
/// </summary>
public sealed class R3F7PocoDispatchUnwrapTests
{
    /// <summary>
    ///     Verifies a POCO event with no handler reports the real error with guidance, not a TargetInvocationException.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_with_poco_and_no_handler_should_throw_unwrapped_with_guidance()
    {
        var registry = new MessageContractRegistry();
        registry.Register<PocoEvent>("tests.events.poco", 1);
        var dispatcher = new EventOutboxDispatcher(
            new ThrowingMediator(new NoHandlerFoundException(typeof(PocoEvent))),
            registry,
            new SystemTextJsonMessageSerializer());

        var act = () => dispatcher.DispatchAsync(new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.events.poco",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Publishing,
            AttemptCount = 1
        });

        var failure = await act.Should().ThrowAsync<LiteBusDispatchException>().ConfigureAwait(false);
        failure.Which.InnerException.Should().BeOfType<NoHandlerFoundException>();
        failure.Which.Message.Should().Contain("Verify the event type is registered");
    }

    private sealed record PocoEvent
    {
        public string Value { get; init; } = string.Empty;
    }

    private sealed class ThrowingMediator : IEventMediator
    {
        private readonly Exception _failure;

        public ThrowingMediator(Exception failure)
        {
            ArgumentNullException.ThrowIfNull(failure);

            _failure = failure;
        }

        public Task PublishAsync<TEvent>(TEvent message, EventMediationSettings? settings = null, CancellationToken cancellationToken = default)
            where TEvent : notnull
        {
            throw _failure;
        }

        public Task PublishAsync(IEvent message, EventMediationSettings? settings = null, CancellationToken cancellationToken = default)
        {
            throw _failure;
        }
    }
}
