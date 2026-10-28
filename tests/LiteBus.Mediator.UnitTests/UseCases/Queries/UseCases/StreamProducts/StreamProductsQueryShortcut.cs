using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.StreamProducts;

public sealed class StreamProductsQueryShortcut : IStreamQueryShortcut<StreamProductsQuery, StreamProductsQueryResult>
{
    public Task<Shortcut<IAsyncEnumerable<StreamProductsQueryResult>>> TryAnswerAsync(
        StreamProductsQuery message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.ExecutedTypes.Add(GetType());

        // Answering a stream without supplying one is a legitimate answer: the caller enumerates nothing.
        return Task.FromResult(message.AnswerFromShortcut
            ? Shortcut<IAsyncEnumerable<StreamProductsQueryResult>>.Answer(
                AsyncEnumerable.Empty<StreamProductsQueryResult>(),
                "answered by the shortcut")
            : Shortcut<IAsyncEnumerable<StreamProductsQueryResult>>.None);
    }
}
