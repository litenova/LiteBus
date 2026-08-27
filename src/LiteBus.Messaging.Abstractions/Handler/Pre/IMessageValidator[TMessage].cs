using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Decides whether a message is well-formed.
/// </summary>
/// <typeparam name="TMessage">The type of message this validator runs for.</typeparam>
/// <remarks>
///     <para>
///         A validator answers "is this well-formed", and nothing else. Whether the caller is allowed to proceed belongs
///         to <see cref="IMessageGuard{TMessage}" />, and whether the answer is already known belongs to
///         <see cref="IMessageShortcut{TMessage}" />.
///     </para>
///     <para>
///         Validation runs after guards and before shortcuts, and the framework fixes that order. Authorization runs
///         first so an unauthorized caller learns nothing from a cache hit about whether a resource exists.
///         Well-formedness runs next so a malformed message cannot claim an idempotency key or collect a cached answer.
///         Priority orders validators among themselves and never moves the stage.
///     </para>
///     <para>
///         Every validator runs, and the stage collects their failures rather than stopping at the first, because a
///         caller fixing a malformed message wants all of them at once. That is the one way this stage differs from the
///         guard stage.
///     </para>
///     <para>
///         Reporting a failure is a return value, not an exception. The mediation reports
///         <see cref="MediationOutcome.Invalid" />, which is kept apart from <see cref="MediationOutcome.Denied" /> because
///         a malformed message is not a refused one and an audit trail should not record it as one. The caller receives
///         whatever an <see cref="IMessageRefusalMapper{TMessage,TMessageResult}" /> supplies, or
///         <see cref="LiteBusMessageInvalidException" /> when none is registered.
///     </para>
/// </remarks>
public interface IMessageValidator<in TMessage> : IMessagePreStageHandler
    where TMessage : notnull
{
    /// <summary>
    ///     Decides whether the message is well-formed.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>
    ///     <see cref="Validity.Valid" /> when nothing is wrong, otherwise a validity carrying every failure this
    ///     validator found.
    /// </returns>
    Task<Validity> ValidateAsync(TMessage message, CancellationToken cancellationToken = default);
}
