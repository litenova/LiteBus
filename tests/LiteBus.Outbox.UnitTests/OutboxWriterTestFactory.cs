using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Builds outbox writers for unit tests using the production envelope factory path.
/// </summary>
internal static class OutboxWriterTestFactory
{
    /// <summary>
    ///     Creates an <see cref="Outbox" /> wired with an envelope factory for tests.
    /// </summary>
    /// <param name="store">The outbox store.</param>
    /// <param name="contractRegistry">The contract registry.</param>
    /// <param name="serializer">The message serializer.</param>
    /// <param name="clock">The time provider.</param>
    /// <param name="payloadProtector">The optional payload protector.</param>
    /// <returns>The configured outbox writer.</returns>
    internal static Outbox Create(
        IOutboxStore store,
        IContractReader contractRegistry,
        IMessageSerializer serializer,
        TimeProvider clock,
        IOutboxPayloadProtector? payloadProtector = null) =>
        new(
            store,
            new OutboxEnvelopeFactory(contractRegistry, serializer, clock, payloadProtector),
            clock);
}
