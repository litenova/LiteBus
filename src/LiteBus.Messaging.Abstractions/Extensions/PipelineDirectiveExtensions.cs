using System;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Translates a stopping directive returned by a gate into what the mediation reports and returns.
/// </summary>
/// <remarks>
///     Every mediation strategy needs the same three answers when a gate stops the pipeline: which outcome to report,
///     what to hand the caller, and when a refusal has to surface as an exception because there is nothing to hand back.
///     Keeping them here is what stops strategies, including custom ones, from drifting apart on the meaning of a denial.
/// </remarks>
public static class PipelineDirectiveExtensions
{
    /// <summary>
    ///     Maps a stopping directive to the outcome the mediation reports.
    /// </summary>
    /// <param name="directive">The directive returned by the gate.</param>
    /// <returns>
    ///     <see cref="MessageOutcome.Denied" /> for a refusal, otherwise <see cref="MessageOutcome.ShortCircuited" />.
    /// </returns>
    public static MessageOutcome ToOutcome(this PipelineDirective directive)
    {
        return directive.Kind == PipelineDirectiveKind.Deny
            ? MessageOutcome.Denied
            : MessageOutcome.ShortCircuited;
    }

    /// <summary>
    ///     Determines whether the directive refuses the message without supplying a value for the caller.
    /// </summary>
    /// <param name="directive">The directive returned by the gate.</param>
    /// <returns><see langword="true" /> when the refusal has to reach the caller as an exception.</returns>
    public static bool IsUnansweredDenial(this PipelineDirective directive)
    {
        return directive.Kind == PipelineDirectiveKind.Deny && !directive.HasResult;
    }

    /// <summary>
    ///     Creates the exception a refusal without a result raises.
    /// </summary>
    /// <param name="directive">The directive returned by the gate.</param>
    /// <param name="messageType">The type of the message that was refused.</param>
    /// <returns>The denial to raise.</returns>
    public static LiteBusMessageDeniedException CreateDenial(this PipelineDirective directive, Type messageType)
    {
        return new LiteBusMessageDeniedException(messageType, directive.Reason ?? "no reason was given");
    }

    /// <summary>
    ///     Reads the result a stopping directive supplies for a message that produces one.
    /// </summary>
    /// <typeparam name="TMessageResult">The result type the caller expects.</typeparam>
    /// <param name="directive">The directive returned by the gate.</param>
    /// <param name="messageType">The concrete runtime type of the message being mediated, used in diagnostics.</param>
    /// <returns>The result the caller receives.</returns>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when the gate stopped a message that produces a result without supplying one, or supplied one of the
    ///     wrong type.
    /// </exception>
    /// <remarks>
    ///     A typed gate makes the wrong type impossible, so the mismatch branch exists for a gate written against the
    ///     untyped contract for a message that does produce a result.
    /// </remarks>
    public static TMessageResult ResolveResult<TMessageResult>(this PipelineDirective directive, Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        if (!directive.HasResult)
        {
            throw new LiteBusConfigurationException(
                $"A gate stopped the mediation of '{messageType.Name}' without supplying the "
                + $"'{typeof(TMessageResult).Name}' the caller expects. Implement "
                + $"IMessageGate<{messageType.Name}, {typeof(TMessageResult).Name}> so the compiler requires the "
                + "result, and pass it to ShortCircuit or Deny.");
        }

        switch (directive.Result)
        {
            case TMessageResult typedResult:
                return typedResult;
            case null:
                return default!;
            default:
                throw new LiteBusConfigurationException(
                    $"A gate for '{messageType.Name}' supplied a result of type "
                    + $"'{directive.Result.GetType().Name}', but the message expects '{typeof(TMessageResult).Name}'.");
        }
    }
}
