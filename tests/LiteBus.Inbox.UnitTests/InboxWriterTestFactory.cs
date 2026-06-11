using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Builds inbox writers for unit tests using the production envelope factory path.
/// </summary>
internal static class InboxWriterTestFactory
{
    /// <summary>
    ///     Creates an <see cref="Inbox" /> wired with an envelope factory for tests.
    /// </summary>
    /// <param name="store">The inbox store.</param>
    /// <param name="contractRegistry">The contract registry.</param>
    /// <param name="serializer">The message serializer.</param>
    /// <param name="clock">The time provider.</param>
    /// <param name="payloadProtector">The optional payload protector.</param>
    /// <returns>The configured inbox writer.</returns>
    internal static Inbox Create(
        IInboxStore store,
        IContractReader contractRegistry,
        IMessageSerializer serializer,
        TimeProvider clock,
        IInboxPayloadProtector? payloadProtector = null) =>
        new(
            store,
            new InboxEnvelopeFactory(contractRegistry, serializer, clock, payloadProtector),
            clock);
}
