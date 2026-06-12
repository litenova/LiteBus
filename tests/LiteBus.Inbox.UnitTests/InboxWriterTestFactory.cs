using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Builds inbox writers for unit tests using the production envelope factory path.
/// </summary>
internal static class InboxWriterTestFactory
{
    /// <summary>
    ///     Creates an <see cref="IInbox" /> wired with an envelope factory for tests.
    /// </summary>
    /// <param name="store">The inbox store.</param>
    /// <param name="contractRegistry">The contract registry.</param>
    /// <param name="serializer">The message serializer.</param>
    /// <param name="clock">The time provider.</param>
    /// <param name="payloadProtector">The optional payload protector.</param>
    /// <returns>The configured inbox writer.</returns>
    internal static IInbox Create(
        IInboxStore store,
        IContractReader contractRegistry,
        IMessageSerializer serializer,
        TimeProvider clock,
        IInboxPayloadProtector? payloadProtector = null)
    {
        return new Inbox(
            store,
            new InboxEnvelopeFactory(contractRegistry, serializer, clock, payloadProtector),
            clock);
    }

    /// <summary>
    ///     Builds a typed acceptance item for writer calls in tests.
    /// </summary>
    /// <typeparam name="TMessage">The compile-time message type.</typeparam>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="metadata">
    ///     Optional acceptance metadata. When omitted, <see cref="InboxAcceptMetadata.Immediate" /> is used.
    /// </param>
    /// <returns>A typed acceptance item ready for <see cref="IInbox.AcceptAsync{TMessage}" />.</returns>
    internal static InboxAcceptItem<TMessage> Item<TMessage>(
        TMessage message,
        InboxAcceptMetadata? metadata = null)
        where TMessage : notnull
    {
        return InboxAcceptItem<TMessage>.From(message, metadata);
    }
}