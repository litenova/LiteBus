using System;
using System.ComponentModel;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Turns a recorded mediation ending into the result a <c>Try</c> mediator method returns.
/// </summary>
/// <remarks>
///     Shared by the command and query mediators so the mapping from outcome to result exists once. Nothing an
///     application writes should name it; it is public because the semantic mediators are separate assemblies.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class MediationResultFactory
{
    /// <summary>
    ///     Builds the result for a message that produces no value.
    /// </summary>
    /// <param name="capture">The ending the mediation strategy recorded.</param>
    /// <returns>The result the caller receives.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="capture" /> is <see langword="null" />.</exception>
    public static MediationResult FromCapture(MediationEndingCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        return capture.Outcome switch
        {
            MediationOutcome.Denied => MediationResult.Denied(capture.Reason, capture.Code),
            MediationOutcome.Invalid => MediationResult.Invalid(capture.Reason, capture.Code, capture.Failures),
            MediationOutcome.Answered => MediationResult.Answered(capture.Reason, capture.Code),
            _ => MediationResult.Succeeded()
        };
    }

    /// <summary>
    ///     Builds the result for a message that produces a value.
    /// </summary>
    /// <typeparam name="TMessageResult">The type of result the message produces.</typeparam>
    /// <param name="capture">The ending the mediation strategy recorded.</param>
    /// <param name="value">The value the mediation produced, when it produced one.</param>
    /// <param name="hasValue">Whether a value was produced.</param>
    /// <returns>The result the caller receives.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="capture" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     A refusal keeps its value when one is present, because a registered refusal mapper supplied it. That is why
    ///     the outcome is read from the capture rather than inferred from whether an exception was raised.
    /// </remarks>
    public static MediationResult<TMessageResult> FromCapture<TMessageResult>(
        MediationEndingCapture capture,
        TMessageResult? value,
        bool hasValue)
    {
        ArgumentNullException.ThrowIfNull(capture);

        return capture.Outcome switch
        {
            MediationOutcome.Denied => MediationResult<TMessageResult>.Denied(
                capture.Reason, capture.Code, value, hasValue),
            MediationOutcome.Invalid => MediationResult<TMessageResult>.Invalid(
                capture.Reason, capture.Code, capture.Failures, value, hasValue),
            MediationOutcome.Answered => MediationResult<TMessageResult>.Answered(
                value!, capture.Reason, capture.Code),
            _ => MediationResult<TMessageResult>.Succeeded(value!)
        };
    }
}
