using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The one place a dispatchable handler contract is declared.
/// </summary>
/// <remarks>
///     <para>
///         Adding the validator stage in v7 meant editing nine places, all of which read the same few facts about a
///         contract. Those facts live here now: <see cref="PipelineDispatch" /> reads the family and the invoker, each
///         descriptor builder reads the contracts to sweep a handler type for, and the stage runner reads the order and
///         the aggregation policy. Adding a role is a row here, an invoker on <see cref="PipelineDispatch" />, and,
///         for a new pre-stage role, a member on <see cref="PreStage" />.
///     </para>
///     <para>
///         Rows are keyed by contract rather than by stage or family, because one stage may accept several contracts.
///         The typed and untyped shortcut contracts are the case that forces it.
///     </para>
///     <para>
///         Every dispatchable contract belongs here, not only the pre-stage ones. Post-handlers, completion handlers,
///         and refusal mappers used to be hand-wired into an if-chain beside the table, which meant two mechanisms for
///         one job and a coin flip over which one the next role landed in.
///     </para>
/// </remarks>
internal static class PipelineContracts
{
    /// <summary>
    ///     Every handler contract the framework dispatches, pre-stage rows first and in stage order.
    /// </summary>
    internal static readonly PipelineContract[] All =
    [
        new(typeof(IMessageGuard<>), PipelineFamily.PreStage, nameof(PipelineDispatch.InvokeGuard), PreStage.Guard),
        new(typeof(IMessageValidator<>), PipelineFamily.PreStage, nameof(PipelineDispatch.InvokeValidator), PreStage.Validator, StageAggregation.CollectFailures),
        new(typeof(IMessageShortcut<>), PipelineFamily.PreStage, nameof(PipelineDispatch.InvokeShortcut), PreStage.Shortcut),
        new(typeof(IMessageShortcut<,>), PipelineFamily.PreStage, nameof(PipelineDispatch.InvokeTypedShortcut), PreStage.Shortcut),
        new(typeof(IMessagePreHandler<>), PipelineFamily.PreStage, nameof(PipelineDispatch.InvokePreHandler), PreStage.PreHandler),
        new(typeof(IMessagePostHandler<,>), PipelineFamily.PostHandler, nameof(PipelineDispatch.InvokePostHandler)),
        new(typeof(IMessageCompletionHandler<>), PipelineFamily.CompletionHandler, nameof(PipelineDispatch.InvokeCompletionHandler)),
        new(typeof(IMessageCompletionHandler<,>), PipelineFamily.CompletionHandler, nameof(PipelineDispatch.InvokeTypedCompletionHandler)),
        new(typeof(IMessageRefusalMapper<,>), PipelineFamily.RefusalMapper, nameof(PipelineDispatch.InvokeRefusalMapperCore))
    ];

    /// <summary>
    ///     The pre stages in the order the framework runs them, which is the order their enum members are declared.
    /// </summary>
    /// <remarks>
    ///     Cached because <see cref="Enum.GetValues{TEnum}" /> allocates, and this is read once per mediation. Deriving
    ///     the order from the enum rather than from a hand-written call sequence is what makes the declared order the
    ///     executed one.
    /// </remarks>
    internal static readonly PreStage[] StagesInOrder = Enum.GetValues<PreStage>().Order().ToArray();

    /// <summary>
    ///     The stages that decide whether a message may proceed, in the order the framework runs them.
    /// </summary>
    /// <remarks>
    ///     A guard answers whether the caller may, and a validator whether the input is well-formed, both by returning
    ///     a value and without acting. A shortcut and a pre-handler act, so they are absent: evaluating a message must
    ///     not claim an idempotency key or run work for a caller who only asked a question. Derived from the same enum
    ///     as <see cref="StagesInOrder" /> so the two cannot disagree about order.
    /// </remarks>
    internal static readonly PreStage[] DecisionStagesInOrder =
        StagesInOrder.Where(static stage => stage is PreStage.Guard or PreStage.Validator).ToArray();

    /// <summary>
    ///     Contracts indexed by their open generic definition, for the lookups on the dispatch path.
    /// </summary>
    private static readonly Dictionary<Type, PipelineContract> ByContract =
        All.ToDictionary(contract => contract.ContractDefinition);

    /// <summary>
    ///     Aggregation policy per stage, derived from the rows so the two cannot disagree.
    /// </summary>
    private static readonly Dictionary<PreStage, StageAggregation> AggregationByStage =
        All.Where(contract => contract.Stage is not null)
           .GroupBy(contract => contract.Stage!.Value)
           .ToDictionary(group => group.Key, group => group.First().Aggregation);

    /// <summary>
    ///     Gets the open generic contracts a descriptor builder should sweep a handler type for.
    /// </summary>
    /// <param name="family">The family the builder produces descriptors for.</param>
    /// <returns>Every declared contract in that family, in declaration order.</returns>
    /// <remarks>
    ///     Descriptor builders read their contracts from here rather than listing them, so a new contract is discovered
    ///     by the builder that owns its family without that builder being edited.
    /// </remarks>
    internal static IEnumerable<Type> ContractDefinitions(PipelineFamily family)
    {
        return All.Where(contract => contract.Family == family)
                  .Select(contract => contract.ContractDefinition);
    }

    /// <summary>
    ///     Finds the row for a contract a handler was discovered under.
    /// </summary>
    /// <param name="contractType">The closed or open contract discovered on the handler.</param>
    /// <returns>The matching row, or <see langword="null" /> when the contract is not dispatched by the pipeline.</returns>
    internal static PipelineContract? Find(Type contractType)
    {
        if (!contractType.IsGenericType)
        {
            return null;
        }

        return ByContract.GetValueOrDefault(contractType.GetGenericTypeDefinition());
    }

    /// <summary>
    ///     Determines whether an open generic contract definition is one the pipeline dispatches.
    /// </summary>
    /// <param name="contractDefinition">The open generic contract definition to test.</param>
    /// <returns><see langword="true" /> when the definition appears in <see cref="All" />.</returns>
    /// <remarks>
    ///     Read by <see cref="MessagingHandlerContracts" /> so an axis builder decides membership from this table
    ///     rather than from its own list, which is what keeps a new role recognised on every axis at once.
    /// </remarks>
    internal static bool IsDispatchable(Type contractDefinition)
    {
        return ByContract.ContainsKey(contractDefinition);
    }

    /// <summary>
    ///     Reads how a stage treats the decisions its handlers return.
    /// </summary>
    /// <param name="stage">The stage being run.</param>
    /// <returns>The aggregation policy declared by that stage's rows.</returns>
    internal static StageAggregation AggregationFor(PreStage stage)
    {
        return AggregationByStage.GetValueOrDefault(stage, StageAggregation.StopAtFirst);
    }
}
