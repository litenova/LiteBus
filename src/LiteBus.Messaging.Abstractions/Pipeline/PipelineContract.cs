using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Everything the framework knows about one dispatchable handler contract.
/// </summary>
/// <param name="ContractDefinition">The open generic contract a handler declares, such as <c>IMessageGuard&lt;&gt;</c>.</param>
/// <param name="Family">The shape of the call the pipeline makes through this contract.</param>
/// <param name="InvokerMethodName">The <see cref="PipelineDispatch" /> method that calls through the contract.</param>
/// <param name="Stage">
///     The pre stage that runs a handler declaring this contract, or <see langword="null" /> when the contract is not a
///     pre-stage contract.
/// </param>
/// <param name="Aggregation">
///     How the stage treats the decisions its handlers return. Meaningful only for
///     <see cref="PipelineFamily.PreStage" />.
/// </param>
internal sealed record PipelineContract(
    Type ContractDefinition,
    PipelineFamily Family,
    string InvokerMethodName,
    PreStage? Stage = null,
    StageAggregation Aggregation = StageAggregation.StopAtFirst);
