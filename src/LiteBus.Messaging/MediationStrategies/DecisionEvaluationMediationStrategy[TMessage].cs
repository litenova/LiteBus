using System;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.MediationStrategies;

/// <summary>
///     Runs the decision stages for a message and reports what they concluded, without performing the message.
/// </summary>
/// <typeparam name="TMessage">The type of message being evaluated.</typeparam>
/// <remarks>
///     <para>
///         Backs the <c>Evaluate</c> mediator methods. It runs guards and validators in the fixed order and stops,
///         which is what lets a user interface ask the pipeline the same question the pipeline will ask rather than
///         calling a parallel authorization method that can drift from it.
///     </para>
///     <para>
///         Nothing after the decision stages runs, so no shortcut claims a key, no pre-handler does work, no main
///         handler executes, and the completion stage does not fire. An evaluation is not a mediation and produces no
///         audit record; the message it is asking about produces one when it is actually sent.
///     </para>
///     <para>
///         A fault a decision stage raises propagates, unless a refusal classifier recognizes it, in which case the
///         evaluation reports the refusal the message would end as when actually sent.
///     </para>
/// </remarks>
public sealed class DecisionEvaluationMediationStrategy<TMessage>
    : IMessageMediationStrategy<TMessage, Task<MediationDecision>>
    where TMessage : notnull
{
    /// <inheritdoc />
    public async Task<MediationDecision> Mediate(
        TMessage message,
        IMessageDependencies messageDependencies,
        IExecutionContext executionContext)
    {
        ArgumentNullException.ThrowIfNull(messageDependencies);
        ArgumentNullException.ThrowIfNull(executionContext);

        using (AmbientExecutionContext.CreateScope(executionContext))
        {
            PipelineDecision decision;

            try
            {
                decision = await messageDependencies
                    .RunAsyncDecisionStages(message, executionContext.CancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception fault) when (MediationExceptionFilters.IsRecoverableMediationException(fault))
            {
                // A guard that raises a recognized fault refuses the message the same way through Evaluate as through
                // Send: the caller asking "may I" gets the refusal rather than the fault.
                if (MediationExceptionFilters.TryClassifyRefusal(
                        messageDependencies.FaultRefusalClassifiers,
                        fault,
                        out var refusal))
                {
                    return refusal.Outcome == MediationOutcome.Invalid
                        ? MediationDecision.Invalid(refusal.Reason, refusal.Code, [])
                        : MediationDecision.Denied(refusal.Reason, refusal.Code);
                }

                throw;
            }

            return decision.Outcome switch
            {
                MediationOutcome.Denied => MediationDecision.Denied(decision.Reason, decision.Code),
                MediationOutcome.Invalid => MediationDecision.Invalid(decision.Reason, decision.Code, decision.Failures),
                _ => MediationDecision.Allowed
            };
        }
    }
}
