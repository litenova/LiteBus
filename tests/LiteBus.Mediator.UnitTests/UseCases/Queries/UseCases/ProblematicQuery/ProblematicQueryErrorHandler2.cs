using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.ProblematicQuery;

/// <summary>
///     Second problematic query error handler used by query module unit tests.
/// </summary>
public sealed class ProblematicQueryErrorHandler2 : IQueryErrorHandler<ProblematicQuery>
{
    /// <inheritdoc />
    public Task HandleErrorAsync(
        MessageErrorContext<ProblematicQuery, object> context,
        CancellationToken cancellationToken = default)
    {
        context.Message.ExecutedTypes.Add(GetType());

        context.Outcome = MessageErrorOutcome.Handled;
        return Task.CompletedTask;
    }
}
