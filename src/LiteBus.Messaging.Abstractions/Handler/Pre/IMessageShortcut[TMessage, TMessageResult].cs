using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a pre-handler that answers a message that produces a result, so the main handler never runs.
/// </summary>
/// <typeparam name="TMessage">The type of message this shortcut runs for.</typeparam>
/// <typeparam name="TMessageResult">The result type of the message, which the answer is typed over.</typeparam>
/// <remarks>
///     <para>
///         Answering means the main handler never runs, so the shortcut has to supply the value the caller receives.
///         Typing the answer over <typeparamref name="TMessageResult" /> is what makes the compiler check that value
///         instead of the pipeline rejecting it at dispatch time. Because <c>ICommand&lt;TResult&gt;</c> derives from
///         <c>ICommand</c>, the untyped contract also compiles for such a message, which analyzer rule LB1019 reports.
///     </para>
///     <para>
///         A cache hit is the usual case. Refusing belongs to a guard, which runs first and reports
///         <see cref="MessageOutcome.Denied" />.
///     </para>
/// </remarks>
public interface IMessageShortcut<in TMessage, TMessageResult> : IMessagePreStageHandler
    where TMessage : notnull
{
    /// <summary>
    ///     Answers the message when its result is already known.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>
    ///     <see cref="Shortcut{TMessageResult}.Answer" /> carrying the result the caller receives, or
    ///     <see cref="Shortcut{TMessageResult}.None" /> to let the mediation proceed.
    /// </returns>
    Task<Shortcut<TMessageResult>> TryAnswerAsync(TMessage message, CancellationToken cancellationToken = default);
}
