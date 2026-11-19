using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Offers a fault a stage raised to the registered refusal exception translators, in registration order.
/// </summary>
/// <remarks>
///     <para>
///         Translating a domain exception sometimes needs services, such as localization or tenant-aware detail, which
///         the synchronous and stateless <see cref="IFaultRefusalClassifier" /> seam cannot reach. Translators are
///         resolved from the mediation scope, so an implementation may read scoped services, and they run after the
///         classifiers: a fault the classifiers already recognize keeps the refusal it always had, and only what they
///         leave unrecognized is offered here.
///     </para>
///     <para>
///         The first translator that recognizes the fault ends the mediation as its refusal, so <c>TrySendAsync</c>,
///         <c>SendAsync</c>, refusal mappers, audit, and HTTP all observe one consistent refusal. With no translator
///         registered this is a pass over an empty list and every fault keeps the behavior it had before translators
///         existed.
///     </para>
/// </remarks>
internal static class RefusalExceptionTranslation
{
    /// <summary>
    ///     Offers a fault to each registered translator until one recognizes it as a domain failure.
    /// </summary>
    /// <param name="translators">The translators to consult, in the order they were registered.</param>
    /// <param name="exception">The fault a stage raised.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>
    ///     The refusal the first matching translator returned, or <see langword="null" /> when no translator
    ///     recognized the fault.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         Cancellation is never translated: an <see cref="OperationCanceledException" /> reports the caller's own
    ///         decision, so translating it would let a refusal swallow a cancellation. An exception that already
    ///         carries a refusal is not offered either, because it is a decision rather than a fault and a second
    ///         translation would rewrite one.
    ///     </para>
    ///     <para>
    ///         A <see cref="MessageFaultException" /> is never offered: it reports a configuration or dependency
    ///         failure an operator must fix, so translating it would report an operator problem as a refusal the
    ///         caller can act on.
    ///     </para>
    ///     <para>
    ///         A translator must not throw. A fault a translator raises escapes the mediation in place of the
    ///         exception it was offered.
    ///     </para>
    /// </remarks>
    public static async ValueTask<Refusal?> TryTranslateAsync(
        IReadOnlyList<IRefusalExceptionTranslator> translators,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(translators);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException
            || exception is MessageFaultException
            || MediationExceptionFilters.IsRefusal(exception))
        {
            return null;
        }

        foreach (var translator in translators)
        {
            var refusal = await translator.TranslateAsync(exception, cancellationToken).ConfigureAwait(false);

            if (refusal.HasValue)
            {
                return refusal;
            }
        }

        return null;
    }
}
