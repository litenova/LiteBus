using System;
using Microsoft.Extensions.Logging;

namespace LiteBus.Commands.UnitOfWork;

/// <summary>
///     High-performance command unit-of-work log messages.
/// </summary>
internal static class CommandUnitOfWorkLogMessages
{
    /// <summary>
    ///     The delegate that writes a commit listener failure event.
    /// </summary>
    private static readonly Action<ILogger, string, Exception> CommitListenerFailedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2301, nameof(CommitListenerFailed)),
            "Commit listener {ListenerType} threw after the command unit of work committed. The data is committed; the listener fault is logged and the mediation still succeeds.");

    /// <summary>
    ///     The delegate that writes a rollback failure event for a mediation that ended without an exception.
    /// </summary>
    private static readonly Action<ILogger, Exception?> RollbackFailedMessage =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2302, nameof(RollbackFailed)),
            "The command unit of work rollback threw after the mediation ended without an exception to attach it to. The staged writes may not have been discarded.");

    /// <summary>
    ///     Writes a commit listener failure event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="listenerType">The full name of the listener type that threw.</param>
    /// <param name="exception">The exception the listener threw.</param>
    internal static void CommitListenerFailed(ILogger logger, string listenerType, Exception exception)
    {
        CommitListenerFailedMessage(logger, listenerType, exception);
    }

    /// <summary>
    ///     Writes a rollback failure event for a mediation that ended without an exception.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="exception">The exception the rollback threw.</param>
    internal static void RollbackFailed(ILogger logger, Exception exception)
    {
        RollbackFailedMessage(logger, exception);
    }
}
