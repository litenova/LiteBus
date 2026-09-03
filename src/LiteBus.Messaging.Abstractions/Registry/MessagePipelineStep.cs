using System;
using System.Globalization;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     One handler in a message's pipeline plan, in the position it will run.
/// </summary>
/// <param name="Stage">The stage that runs it, such as <c>guard</c> or <c>completion</c>.</param>
/// <param name="Priority">The priority that orders it inside its stage.</param>
/// <param name="HandlerType">The handler type that will be resolved and invoked.</param>
/// <param name="ContractType">The closed contract the pipeline dispatches through.</param>
/// <param name="IsIndirect">
///     Whether the handler was registered for a base type or marker interface rather than for the message itself.
/// </param>
/// <param name="IsClosedOpenGeneric">Whether the handler is an open generic the registry closed over this message.</param>
public sealed record MessagePipelineStep(
    string Stage,
    int Priority,
    Type HandlerType,
    Type ContractType,
    bool IsIndirect,
    bool IsClosedOpenGeneric)
{
    /// <summary>
    ///     Renders the step as one aligned line of the plan.
    /// </summary>
    /// <returns>The rendered step.</returns>
    public override string ToString()
    {
        var origin = (IsIndirect, IsClosedOpenGeneric) switch
        {
            (true, true) => "  (open generic, indirect)",
            (true, false) => "  (indirect)",
            (false, true) => "  (open generic)",
            _ => string.Empty
        };

        return $"  {Stage,-12}{Priority.ToString(CultureInfo.InvariantCulture),9}  {HandlerType.Name}{origin}";
    }
}
