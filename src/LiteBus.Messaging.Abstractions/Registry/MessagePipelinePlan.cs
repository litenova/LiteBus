using System;
using System.Collections.Generic;
using System.Text;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Everything that will run for one message, in the order it will run.
/// </summary>
/// <remarks>
///     <para>
///         Answers the question a codebase with a hundred messages, two open generic guards, an audit writer, an
///         idempotency handler and a commit cannot otherwise answer: what actually runs for this one. Without it the
///         honest answer is to read the registry in a debugger.
///     </para>
///     <para>
///         Read from the registry rather than computed at compile time, deliberately. The registry is the only thing
///         that knows about open generics closed over the message, handlers registered against a base type, tag
///         filtering and priority ties, and a compile-time plan would have to reimplement all of that and would then
///         disagree with the runtime in exactly the cases that matter.
///     </para>
///     <para>
///         Tags are not applied. The plan describes what is registered for the message; a mediation that filters by
///         tag runs a subset of it.
///     </para>
/// </remarks>
public sealed record MessagePipelinePlan
{
    /// <summary>
    ///     Gets the message the plan describes.
    /// </summary>
    public required Type MessageType { get; init; }

    /// <summary>
    ///     Gets the result type the message declares, when it declares one.
    /// </summary>
    public Type? MessageResultType { get; init; }

    /// <summary>
    ///     Gets every handler that will run, in the order it will run.
    /// </summary>
    /// <value>
    ///     Ordered by stage first and then by the priority that orders handlers inside a stage. Two handlers of equal
    ///     priority appear in registration order, which is the order the pipeline runs them in.
    /// </value>
    public required IReadOnlyList<MessagePipelineStep> Steps { get; init; }

    /// <summary>
    ///     Renders the plan as the block a log line or a test failure shows.
    /// </summary>
    /// <returns>The rendered plan.</returns>
    public override string ToString()
    {
        var report = new StringBuilder(MessageType.Name);

        if (MessageResultType is not null)
        {
            report.Append(" -> ").Append(MessageResultType.Name);
        }

        if (Steps.Count == 0)
        {
            return report.Append(Environment.NewLine)
                         .Append("  nothing is registered for this message")
                         .ToString();
        }

        foreach (var step in Steps)
        {
            report.Append(Environment.NewLine).Append(step);
        }

        return report.ToString();
    }
}
