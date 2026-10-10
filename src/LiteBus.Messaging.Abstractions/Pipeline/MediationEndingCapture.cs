using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Carries how a mediation ended back to the caller that asked for a result instead of an exception.
/// </summary>
/// <remarks>
///     <para>
///         Placed in <see cref="IExecutionContext.Items" /> under <see cref="ItemKey" /> by a <c>Try</c> mediator
///         method, and filled in by the mediation strategy from the decision it already computed. The strategy is the
///         only thing that knows the outcome, and the caller is the only thing that needs it as a value, so something
///         has to travel between them.
///     </para>
///     <para>
///         It is not read from the exception instead, because an application with a registered
///         <see cref="IMessageRefusalMapper{TMessage,TMessageResult}" /> gets no exception at all: the mapped value is
///         returned and a denial would read as a success. The capture reports the outcome either way.
///     </para>
///     <para>
///         Nothing an application writes should name this. It is public because the mediation strategies and the
///         semantic mediators are separate assemblies.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class MediationEndingCapture
{
    /// <summary>
    ///     The execution-context item key the capture is passed under.
    /// </summary>
    public const string ItemKey = "__LiteBus.Mediation.Ending";

    /// <summary>
    ///     Gets or sets how the mediation ended.
    /// </summary>
    public MediationOutcome Outcome { get; set; } = MediationOutcome.Succeeded;

    /// <summary>
    ///     Gets or sets why the pipeline stopped, when it stopped.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    ///     Gets or sets the machine-readable code the decision supplied.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    ///     Gets or sets the failures the validator stage collected.
    /// </summary>
    public IReadOnlyList<ValidationFailure> Failures { get; set; } = [];

    /// <summary>
    ///     Gets the pre stages that ran, in the order they ran.
    /// </summary>
    /// <remarks>
    ///     This is the part of a mediation no consumer can observe from outside. When the whole point of the library
    ///     is that behavior moved into named stages, asserting which stages ran is exactly what a test of that
    ///     behavior wants, and nothing but the stage runner knows it.
    /// </remarks>
    public List<PreStage> StagesRun { get; } = [];

    /// <summary>
    ///     Gets or sets the handler that stopped the mediation, when one did.
    /// </summary>
    public Type? DecidedBy { get; set; }

    /// <summary>
    ///     Records the ending a mediation strategy observed.
    /// </summary>
    /// <param name="executionContext">The execution context the mediation ran under.</param>
    /// <param name="decision">The decision that stopped the pipeline.</param>
    /// <remarks>
    ///     A no-op when no capture is present, which is every ordinary mediation. Only a <c>Try</c> call installs one,
    ///     so the cost on the normal path is one dictionary lookup.
    /// </remarks>
    public static void Record(IExecutionContext executionContext, PipelineDecision decision)
    {
        if (Find(executionContext) is not { } capture)
        {
            return;
        }

        capture.Outcome = decision.Outcome;
        capture.Reason = decision.Reason;
        capture.Code = decision.Code;
        capture.Failures = decision.Failures;
        capture.DecidedBy = decision.AnsweredBy;
    }

    /// <summary>
    ///     Records that one pre stage ran.
    /// </summary>
    /// <param name="executionContext">The execution context the mediation is running under.</param>
    /// <param name="stage">The stage that ran.</param>
    /// <remarks>
    ///     A no-op when no capture is present, which is every ordinary mediation.
    /// </remarks>
    public static void RecordStage(IExecutionContext executionContext, PreStage stage)
    {
        Find(executionContext)?.StagesRun.Add(stage);
    }

    /// <summary>
    ///     Reads the capture installed for this mediation, if any.
    /// </summary>
    /// <param name="executionContext">The execution context the mediation is running under.</param>
    /// <returns>The capture, or <see langword="null" /> when the caller installed none.</returns>
    private static MediationEndingCapture? Find(IExecutionContext executionContext)
    {
        return executionContext.Items.TryGetValue(ItemKey, out var stored)
            ? stored as MediationEndingCapture
            : null;
    }
}
