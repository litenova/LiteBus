namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Turns a refused message into the result the caller expects, for applications that model failure as a value.
/// </summary>
/// <typeparam name="TMessage">The type of message this mapper covers.</typeparam>
/// <typeparam name="TMessageResult">The type of result the message produces.</typeparam>
/// <remarks>
///     <para>
///         Without a mapper, a refusal reaches the caller as <see cref="LiteBusMessageDeniedException" /> or
///         <see cref="LiteBusMessageInvalidException" />. Register a mapper when the application returns a failed result
///         object instead, and one registration then covers every guard and validator for the message. A guard supplies
///         only a reason and an optional code, so the shape of a refused result is defined once rather than repeated in
///         each decision.
///     </para>
///     <para>
///         Register the mapper against a base type or interface to cover a whole axis: a mapper for
///         <c>ICommand</c> returning <c>Result</c> covers every command that produces one. A mapper registered for the
///         concrete message type wins over a mapper registered for a base type, matching how the rest of the pipeline
///         resolves direct against indirect registrations.
///     </para>
///     <para>
///         Mapping is synchronous and must stay pure. It runs on the refusal path, where reaching for a database or an
///         HTTP call is exactly what the decision was trying to avoid.
///     </para>
/// </remarks>
public interface IMessageRefusalMapper<in TMessage, out TMessageResult> : IMessageRefusalMapper
    where TMessage : notnull
{
    /// <summary>
    ///     Maps a refusal to the result the caller receives.
    /// </summary>
    /// <param name="message">The message that was refused.</param>
    /// <param name="refusal">The outcome, reason, and code the decision supplied.</param>
    /// <returns>The result returned to the caller in place of the one the main handler would have produced.</returns>
    TMessageResult Map(TMessage message, Refusal refusal);
}
