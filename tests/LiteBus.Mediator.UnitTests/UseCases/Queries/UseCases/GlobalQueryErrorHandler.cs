using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.Testing;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases;

/// <summary>
///     Global query error handler used by query module unit tests.
/// </summary>
public class GlobalQueryErrorHandler : IQueryErrorHandler
{
    /// <inheritdoc />
    public Task HandleErrorAsync(IQuery message, object? messageResult, Exception exception, CancellationToken cancellationToken = default)
    {
        if (message is IAuditableQuery auditableQuery)
        {
            auditableQuery.ExecutedTypes.Add(GetType());
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    object IMessageErrorHandler.HandleError(MessageErrorContext context)
    {
        var typed = context.AsTyped<IQuery, object?>();
        var task = HandleErrorAsync(
            typed.Message,
            typed.MessageResult,
            typed.Exception,
            AmbientExecutionContext.Current.CancellationToken);

        return LegacyErrorHandlerSupport.MarkHandled(context, task);
    }
}
