using LiteBus.Messaging.Abstractions;

namespace LiteBus.Testing;

/// <summary>
///     Marks legacy error handlers as handled for tests that expect suppression after the error chain.
/// </summary>
public static class LegacyErrorHandlerSupport
{
    /// <summary>
    ///     Marks the error context handled and returns the handler task.
    /// </summary>
    /// <param name="context">The error context observed during mediation.</param>
    /// <param name="task">The legacy handler task.</param>
    /// <returns>The supplied handler task.</returns>
    public static object MarkHandled(MessageErrorContext context, Task task)
    {
        context.Outcome = MessageErrorOutcome.Handled;
        return task;
    }
}
