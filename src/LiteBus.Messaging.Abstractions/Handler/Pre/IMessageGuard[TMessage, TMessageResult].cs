using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a pre-handler that decides whether a message is permitted to proceed, and can hand the caller a
///     refusal value instead of raising an exception.
/// </summary>
/// <typeparam name="TMessage">The type of message this guard runs for.</typeparam>
/// <typeparam name="TMessageResult">The result type of the message, which the refusal value is typed over.</typeparam>
/// <remarks>
///     <para>
///         This contract is opt-in. <see cref="IMessageGuard{TMessage}" /> is correct for a message that produces a
///         result too, because a refusal does not owe the caller the value the main handler would have produced; the
///         refusal simply reaches the caller as <see cref="LiteBusMessageDeniedException" /> there. Implement this shape
///         when the application models failure as a value, so the caller receives a failed result object instead.
///     </para>
///     <para>
///         Typing the verdict over <typeparamref name="TMessageResult" /> is what makes the compiler check that value.
///     </para>
/// </remarks>
public interface IMessageGuard<in TMessage, TMessageResult> : IMessagePreHandler
    where TMessage : notnull
{
    /// <summary>
    ///     Checks whether the message is permitted to proceed.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The verdict that allows the message, or refuses it with the value the caller receives.</returns>
    Task<Verdict<TMessageResult>> CheckAsync(TMessage message, CancellationToken cancellationToken = default);
}
