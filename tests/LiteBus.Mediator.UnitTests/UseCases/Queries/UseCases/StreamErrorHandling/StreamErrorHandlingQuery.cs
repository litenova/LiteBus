using System.Runtime.CompilerServices;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.StreamErrorHandling;

/// <summary>
///     A stream query used to verify error handler argument semantics.
/// </summary>
public sealed class StreamErrorHandlingQuery : IAuditableQuery, IStreamQuery<StreamErrorHandlingQueryResult>
{
    /// <inheritdoc />
    public Guid CorrelationId { get; } = Guid.NewGuid();

    /// <summary>
    ///     Gets or sets the message result observed by the error handler.
    /// </summary>
    public object? ObservedErrorHandlerMessageResult { get; set; }

    /// <inheritdoc />
    public List<Type> ExecutedTypes { get; } = [];
}

/// <summary>
///     The result type for <see cref="StreamErrorHandlingQuery" />.
/// </summary>
public sealed class StreamErrorHandlingQueryResult
{
    /// <summary>
    ///     Gets or sets the correlation identifier.
    /// </summary>
    public Guid CorrelationId { get; init; }
}

/// <summary>
///     Throws during stream enumeration after producing one item.
/// </summary>
public sealed class StreamErrorHandlingQueryHandler
    : IStreamQueryHandler<StreamErrorHandlingQuery, StreamErrorHandlingQueryResult>
{
    /// <inheritdoc />
    public async IAsyncEnumerable<StreamErrorHandlingQueryResult> StreamAsync(
        StreamErrorHandlingQuery message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        yield return await Task.FromResult(new StreamErrorHandlingQueryResult { CorrelationId = message.CorrelationId }).ConfigureAwait(false);
        throw new InvalidOperationException("Stream enumeration failed.");
    }
}

/// <summary>
///     Records the message result passed to the direct error handler.
/// </summary>
public sealed class StreamErrorHandlingQueryErrorHandler : IQueryErrorHandler<StreamErrorHandlingQuery>
{
    /// <inheritdoc />
    public Task HandleErrorAsync(
        MessageErrorContext<StreamErrorHandlingQuery, object> context,
        CancellationToken cancellationToken = default)
    {
        context.Message.ExecutedTypes.Add(GetType());
        context.Message.ObservedErrorHandlerMessageResult = context.MessageResult;
        context.Outcome = MessageErrorOutcome.Handled;
        return Task.CompletedTask;
    }
}
