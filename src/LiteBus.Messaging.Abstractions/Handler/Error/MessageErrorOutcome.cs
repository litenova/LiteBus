namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Describes how the mediation pipeline should proceed after error handlers run.
/// </summary>
public enum MessageErrorOutcome
{
    /// <summary>
    ///     The exception was not handled; the pipeline rethrows the original exception.
    /// </summary>
    Unhandled = 0,

    /// <summary>
    ///     The exception was handled; the pipeline suppresses the exception and may use <see cref="MessageErrorContext.HandledResult" />.
    /// </summary>
    Handled = 1
}
