using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.Testing;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.ProblematicQuery;

/// <summary>
///     Second problematic query error handler used by query module unit tests.
/// </summary>
public sealed class ProblematicQueryErrorHandler2 : IQueryErrorHandler<ProblematicQuery>
{
    /// <inheritdoc />
    public Task HandleErrorAsync(ProblematicQuery message, object? messageResult, Exception exception, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    object IMessageErrorHandler.HandleError(MessageErrorContext context)
    {
        var typed = context.AsTyped<ProblematicQuery, object?>();
        var task = HandleErrorAsync(
            typed.Message,
            typed.MessageResult,
            typed.Exception,
            AmbientExecutionContext.Current.CancellationToken);

        return LegacyErrorHandlerSupport.MarkHandled(context, task);
    }
}
