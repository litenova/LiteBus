using System.Collections.Generic;
using System.Threading;

namespace LiteBus.Messaging.Abstractions;

public sealed class MediateOptions<TMessage, TMessageResult>
{
    public required IMessageResolveStrategy MessageResolveStrategy { get; init; }

    public required IMessageMediationStrategy<TMessage, TMessageResult> MessageMediationStrategy { get; init; }

    public required CancellationToken CancellationToken { get; init; } = CancellationToken.None;

    // TODO: temporary placement for tags. Need to find a better place
    public required IEnumerable<string> Tags { get; init; }

    /// <summary>
    /// Indicates whether to register plain messages on spot. Plain messages are messages that do not implement any interfaces.
    /// </summary>
    /// <remarks>
    /// 
    /// </remarks>
    public bool RegisterPlainMessagesOnSpot { get; init; } = false;
}