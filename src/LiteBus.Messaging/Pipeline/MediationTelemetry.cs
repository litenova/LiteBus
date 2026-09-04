using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Pipeline;

/// <summary>
///     Records what one mediation did: a span, its duration, its outcome, and which stage stopped it.
/// </summary>
/// <remarks>
///     <para>
///         Static, like the other LiteBus telemetry types, because an <c>ActivitySource</c> and a <c>Meter</c> are
///         process-wide by design and creating one per mediation would defeat the listener model.
///     </para>
///     <para>
///         Everything it records is gated on <see cref="Options" />, which the messaging module sets once at
///         composition. The default path is one span start against a source with no listener, one histogram
///         measurement, and one counter increment.
///     </para>
/// </remarks>
internal static class MediationTelemetry
{
    /// <summary>
    ///     The activity source used for mediation spans.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(LiteBusMediationTelemetry.ActivitySourceName);

    /// <summary>
    ///     The meter used for mediation instruments.
    /// </summary>
    private static readonly Meter Meter = new(LiteBusMediationTelemetry.MeterName);

    /// <summary>
    ///     The histogram of total mediation duration in milliseconds.
    /// </summary>
    private static readonly Histogram<double> Duration =
        Meter.CreateHistogram<double>(LiteBusMediationTelemetry.DurationInstrumentName, "ms");

    /// <summary>
    ///     The counter of completed mediations.
    /// </summary>
    private static readonly Counter<long> Count =
        Meter.CreateCounter<long>(LiteBusMediationTelemetry.CountInstrumentName);

    /// <summary>
    ///     The histogram of per-stage duration in milliseconds.
    /// </summary>
    private static readonly Histogram<double> StageDuration =
        Meter.CreateHistogram<double>(LiteBusMediationTelemetry.StageDurationInstrumentName, "ms");

    /// <summary>
    ///     The counter of stage decisions that stopped a mediation.
    /// </summary>
    private static readonly Counter<long> Decisions =
        Meter.CreateCounter<long>(LiteBusMediationTelemetry.DecisionsInstrumentName);

    /// <summary>
    ///     Gets or sets what is recorded.
    /// </summary>
    /// <remarks>
    ///     Set once by the messaging module during composition. A process hosting two LiteBus compositions shares one
    ///     setting, which is the same trade every static telemetry type in the library already makes.
    /// </remarks>
    public static MediationTelemetryOptions Options { get; set; } = new();

    /// <summary>
    ///     Starts the span for one mediation.
    /// </summary>
    /// <param name="messageType">The message being mediated.</param>
    /// <returns>The span, or <see langword="null" /> when spans are off or nothing is listening.</returns>
    public static Activity? StartMediation(Type messageType)
    {
        if (!Options.Spans)
        {
            return null;
        }

        var activity = ActivitySource.StartActivity($"mediate {messageType.Name}", ActivityKind.Internal);
        activity?.SetTag(LiteBusMediationTelemetry.MessageAttributeName, messageType.Name);

        return activity;
    }

    /// <summary>
    ///     Starts the span for one pre stage.
    /// </summary>
    /// <param name="stage">The stage about to run.</param>
    /// <param name="messageType">The message being mediated.</param>
    /// <returns>The span, or <see langword="null" /> when stage spans are off or nothing is listening.</returns>
    public static Activity? StartStage(PreStage stage, Type messageType)
    {
        if (!Options.StageSpans)
        {
            return null;
        }

        var activity = ActivitySource.StartActivity($"{StageName(stage)} {messageType.Name}", ActivityKind.Internal);
        activity?.SetTag(LiteBusMediationTelemetry.StageAttributeName, StageName(stage));

        return activity;
    }

    /// <summary>
    ///     Records how one pre stage went.
    /// </summary>
    /// <param name="stage">The stage that ran.</param>
    /// <param name="messageType">The message being mediated.</param>
    /// <param name="elapsed">How long the stage took.</param>
    /// <param name="decision">What the stage decided.</param>
    public static void RecordStage(PreStage stage, Type messageType, TimeSpan elapsed, PipelineDecision decision)
    {
        if (Options.StageMetrics)
        {
            StageDuration.Record(
                elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>(LiteBusMediationTelemetry.MessageAttributeName, messageType.Name),
                new KeyValuePair<string, object?>(LiteBusMediationTelemetry.StageAttributeName, StageName(stage)));
        }

        if (!Options.Metrics || !decision.StopsPipeline)
        {
            return;
        }

        // Recorded whenever metrics are on, not only with stage metrics: which stage stopped a message is the
        // question the whole instrument set exists to answer, and it is one measurement per stopped mediation rather
        // than one per stage per message.
        Decisions.Add(
            1,
            new KeyValuePair<string, object?>(LiteBusMediationTelemetry.MessageAttributeName, messageType.Name),
            new KeyValuePair<string, object?>(LiteBusMediationTelemetry.StageAttributeName, StageName(stage)),
            new KeyValuePair<string, object?>(
                LiteBusMediationTelemetry.OutcomeAttributeName,
                decision.Outcome.ToString()),
            new KeyValuePair<string, object?>(
                LiteBusMediationTelemetry.DecidedByAttributeName,
                DecidedBy(decision)));
    }

    /// <summary>
    ///     Records how one mediation ended, and annotates its span.
    /// </summary>
    /// <param name="activity">The mediation span, when one was started.</param>
    /// <param name="messageType">The message that was mediated.</param>
    /// <param name="outcome">How the mediation ended.</param>
    /// <param name="code">The machine-readable code a decision supplied, when it supplied one.</param>
    /// <param name="elapsed">How long the mediation took.</param>
    public static void RecordMediation(
        Activity? activity,
        Type messageType,
        MediationOutcome outcome,
        string? code,
        TimeSpan elapsed)
    {
        if (activity is not null)
        {
            activity.SetTag(LiteBusMediationTelemetry.OutcomeAttributeName, outcome.ToString());

            if (code is not null)
            {
                activity.SetTag(LiteBusMediationTelemetry.CodeAttributeName, code);
            }

            // A denial is a decision rather than an error, so only a fault sets the error status. A span coloured red
            // for every refused request makes a trace view useless for finding the requests that actually broke.
            activity.SetStatus(
                outcome is MediationOutcome.Failed ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
        }

        if (!Options.Metrics)
        {
            return;
        }

        var message = new KeyValuePair<string, object?>(
            LiteBusMediationTelemetry.MessageAttributeName,
            messageType.Name);

        var outcomeTag = new KeyValuePair<string, object?>(
            LiteBusMediationTelemetry.OutcomeAttributeName,
            outcome.ToString());

        Duration.Record(elapsed.TotalMilliseconds, message, outcomeTag);

        if (code is null)
        {
            Count.Add(1, message, outcomeTag);
            return;
        }

        Count.Add(
            1,
            message,
            outcomeTag,
            new KeyValuePair<string, object?>(LiteBusMediationTelemetry.CodeAttributeName, code));
    }

    /// <summary>
    ///     Names the handler that stopped a mediation.
    /// </summary>
    /// <param name="decision">The decision that stopped it.</param>
    /// <returns>The handler type name, or a stand-in when the stage does not record one.</returns>
    /// <remarks>
    ///     Only the shortcut stage records the deciding handler today, because that is the stage where naming it
    ///     resolves a genuine ambiguity between several globally registered shortcuts. A guard stage stops at the first
    ///     denial and the reason usually identifies it.
    /// </remarks>
    private static string DecidedBy(PipelineDecision decision)
    {
        return decision.AnsweredBy?.Name ?? "unnamed";
    }

    /// <summary>
    ///     Names a pre stage for a tag value.
    /// </summary>
    /// <param name="stage">The stage to name.</param>
    /// <returns>The lower-case stage name.</returns>
    private static string StageName(PreStage stage)
    {
        return stage.ToString().ToLower(CultureInfo.InvariantCulture);
    }
}
