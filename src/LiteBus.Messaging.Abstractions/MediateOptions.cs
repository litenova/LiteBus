using System.Threading;

namespace LiteBus.Messaging.Abstractions;

public sealed class MediateOptions<TMessage, TMessageResult>
{
    public required IMessageResolveStrategy MessageResolveStrategy { get; init; }

    public required IMessageMediationStrategy<TMessage, TMessageResult> MessageMediationStrategy { get; init; }

    public required CancellationToken CancellationToken { get; init; } = CancellationToken.None;
}