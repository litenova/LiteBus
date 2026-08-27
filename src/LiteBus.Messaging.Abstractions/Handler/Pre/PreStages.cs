using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The one place a pre-stage role is declared.
/// </summary>
/// <remarks>
///     <para>
///         Adding the validator stage in v7 meant editing nine places, all of which read the same four facts about a
///         stage. Those facts live here now: dispatch reads the stage and the invoker, the descriptor builder reads the
///         contracts to sweep for, and the stage runner reads the order and the aggregation policy. A new role is a row
///         here, an invoker on <see cref="PipelineDispatch" />, and a member on <see cref="PipelineStage" />.
///     </para>
///     <para>
///         Rows are keyed by contract rather than by stage, because one stage may accept several contracts. The typed
///         and untyped shortcut contracts are the case that forces it.
///     </para>
/// </remarks>
internal static class PreStages
{
    /// <summary>
    ///     Every pre-stage contract the framework dispatches, in stage order.
    /// </summary>
    internal static readonly PreStageDefinition[] All =
    [
        new(typeof(IMessageGuard<>), PipelineStage.Guard, nameof(PipelineDispatch.InvokeGuard), StageAggregation.StopAtFirst),
        new(typeof(IMessageValidator<>), PipelineStage.Validator, nameof(PipelineDispatch.InvokeValidator), StageAggregation.CollectFailures),
        new(typeof(IMessageShortcut<>), PipelineStage.Shortcut, nameof(PipelineDispatch.InvokeShortcut), StageAggregation.StopAtFirst),
        new(typeof(IMessageShortcut<,>), PipelineStage.Shortcut, nameof(PipelineDispatch.InvokeTypedShortcut), StageAggregation.StopAtFirst),
        new(typeof(IMessagePreHandler<>), PipelineStage.PreHandler, nameof(PipelineDispatch.InvokePreHandler), StageAggregation.StopAtFirst)
    ];

    /// <summary>
    ///     The stages in the order the framework runs them, which is the order their enum members are declared.
    /// </summary>
    /// <remarks>
    ///     Cached because <see cref="Enum.GetValues{TEnum}" /> allocates, and this is read once per mediation. Deriving
    ///     the order from the enum rather than from a hand-written call sequence is what makes the declared order the
    ///     executed one.
    /// </remarks>
    internal static readonly PipelineStage[] InOrder = Enum.GetValues<PipelineStage>().Order().ToArray();

    /// <summary>
    ///     Contracts indexed by their open generic definition, for the lookups on the dispatch path.
    /// </summary>
    private static readonly Dictionary<Type, PreStageDefinition> ByContract =
        All.ToDictionary(definition => definition.ContractDefinition);

    /// <summary>
    ///     Aggregation policy per stage, derived from the rows so the two cannot disagree.
    /// </summary>
    private static readonly Dictionary<PipelineStage, StageAggregation> AggregationByStage =
        All.GroupBy(definition => definition.Stage)
           .ToDictionary(group => group.Key, group => group.First().Aggregation);

    /// <summary>
    ///     Gets the open generic contracts a descriptor builder should sweep a handler type for.
    /// </summary>
    /// <returns>Every declared pre-stage contract, in stage order.</returns>
    internal static IEnumerable<Type> ContractDefinitions()
    {
        return All.Select(definition => definition.ContractDefinition);
    }

    /// <summary>
    ///     Finds the definition for a contract a handler was discovered under.
    /// </summary>
    /// <param name="contractType">The closed or open contract discovered on the handler.</param>
    /// <returns>The matching definition, or <see langword="null" /> when the contract is not a pre-stage contract.</returns>
    internal static PreStageDefinition? Find(Type contractType)
    {
        if (!contractType.IsGenericType)
        {
            return null;
        }

        return ByContract.GetValueOrDefault(contractType.GetGenericTypeDefinition());
    }

    /// <summary>
    ///     Reads how a stage treats the decisions its handlers return.
    /// </summary>
    /// <param name="stage">The stage being run.</param>
    /// <returns>The aggregation policy declared by that stage's rows.</returns>
    internal static StageAggregation AggregationFor(PipelineStage stage)
    {
        return AggregationByStage.GetValueOrDefault(stage, StageAggregation.StopAtFirst);
    }
}
