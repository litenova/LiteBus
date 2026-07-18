using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases;

/// <summary>
///     Global query error handler used by query module unit tests.
/// </summary>
public class GlobalQueryErrorHandler : IQueryErrorHandler
{
    /// <inheritdoc />
    public Task HandleErrorAsync(
        MessageErrorContext<IQuery, object> context,
        CancellationToken cancellationToken = default)
    {
        if (context.Message is IAuditableQuery auditableQuery)
        {
            auditableQuery.ExecutedTypes.Add(GetType());
        }

        context.Outcome = MessageErrorOutcome.Handled;
        return Task.CompletedTask;
    }
}
