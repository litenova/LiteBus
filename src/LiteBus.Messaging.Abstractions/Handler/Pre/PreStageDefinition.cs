using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Everything the framework knows about one pre-stage contract.
/// </summary>
/// <param name="ContractDefinition">The open generic contract a handler declares, such as <c>IMessageGuard&lt;&gt;</c>.</param>
/// <param name="Stage">The stage that runs a handler declaring this contract.</param>
/// <param name="InvokerMethodName">The <see cref="PipelineDispatch" /> method that calls through the contract.</param>
/// <param name="Aggregation">How the stage treats the decisions its handlers return.</param>
internal sealed record PreStageDefinition(
    Type ContractDefinition,
    PipelineStage Stage,
    string InvokerMethodName,
    StageAggregation Aggregation);
