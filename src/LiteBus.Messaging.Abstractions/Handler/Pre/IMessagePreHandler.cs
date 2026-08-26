using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The dispatch entry point the mediation pipeline uses to run a pre-handler.
/// </summary>
/// <remarks>
///     <para>
///         Do not implement this interface directly. Implement <see cref="IMessagePreHandler{TMessage}" /> for a
///         pre-handler that validates, authorizes, or enriches, or
///         <see cref="IShortCircuitingPreHandler{TMessage}" /> for one that may stop the pipeline. Both supply this
///         member for you.
///     </para>
///     <para>
///         Every pre-handler returns a <see cref="PipelineDirective" /> through this entry point, which is what lets the
///         pipeline invoke both kinds through one virtual call with no reflection. A pre-handler that cannot
///         short-circuit always reports <see cref="PipelineDirective.Continue" />.
///     </para>
/// </remarks>
public interface IMessagePreHandler
{
    /// <summary>
    ///     Runs the pre-handler and reports whether the pipeline should proceed.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The directive that tells the pipeline whether to continue.</returns>
    Task<PipelineDirective> PreHandleAsync(object message, CancellationToken cancellationToken);
}
