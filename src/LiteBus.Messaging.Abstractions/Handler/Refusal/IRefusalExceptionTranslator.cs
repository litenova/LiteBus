using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Translates a domain exception raised by a handler into the refusal callers observe.
/// </summary>
/// <remarks>
///     <para>
///         Application exceptions become refusals inside the mediation pipeline through translators, so HTTP, gRPC,
///         and in-process callers all observe the same refusal. Transports never catch application exception types.
///     </para>
///     <para>
///         Translators are resolved from the dispatch scope when the pipeline invokes them, so an implementation may
///         read scoped services. They are consulted in registration order and the first translation wins. With no
///         translator registered, exceptions pass through as faults.
///     </para>
///     <para>
///         A translator is a mapping over the exception: it returns <see langword="null" /> for exceptions it does not recognize,
///         and it must not throw. A fault a translator raises escapes the mediation in place of the exception it was
///         offered.
///     </para>
/// </remarks>
public interface IRefusalExceptionTranslator
{
    /// <summary>
    ///     Translates an exception into a refusal when it is a domain failure this translator recognizes.
    /// </summary>
    /// <param name="exception">The exception a handler raised.</param>
    /// <param name="cancellationToken">The token signalled when translation is cancelled.</param>
    /// <returns>
    ///     The refusal the mediation ends as, or <see langword="null" /> when the translator does not recognize the
    ///     exception.
    /// </returns>
    ValueTask<Refusal?> TranslateAsync(
        Exception exception,
        CancellationToken cancellationToken);
}
