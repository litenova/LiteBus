using System.Reflection;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Guards the invariants the pipeline contract table relies on but cannot express in its own shape.
/// </summary>
/// <remarks>
///     The table is the one place a dispatchable handler contract is declared, and the dispatch factory, four descriptor
///     builders, and the stage runner all read from it. These assertions cover the assumptions those readers make, so a
///     wrong row fails here rather than as strange behavior somewhere else.
/// </remarks>
public sealed class PipelineContractTableTests
{
    [Fact]
    public void Every_declared_stage_has_at_least_one_contract()
    {
        var declared = PipelineContracts.All
            .Where(contract => contract.Stage is not null)
            .Select(contract => contract.Stage!.Value)
            .Distinct();

        // A stage with no contract can never run, so it would be a member nothing could ever land in.
        declared.Should().BeEquivalentTo(PipelineContracts.StagesInOrder);
    }

    [Fact]
    public void Rows_sharing_a_stage_agree_on_how_it_aggregates()
    {
        var stageRows = PipelineContracts.All.Where(contract => contract.Stage is not null);

        foreach (var group in stageRows.GroupBy(contract => contract.Stage!.Value))
        {
            // AggregationFor reads the first row for a stage. Two rows disagreeing would make which one wins depend on
            // declaration order, which is exactly the class of accident the table exists to remove.
            group.Select(contract => contract.Aggregation).Distinct().Should().ContainSingle(
                $"stage {group.Key} must aggregate one way");
        }
    }

    [Fact]
    public void Pre_stage_rows_are_declared_in_the_order_their_stages_run()
    {
        var stagesInRowOrder = PipelineContracts.All
            .Where(contract => contract.Stage is not null)
            .Select(contract => (int) contract.Stage!.Value)
            .ToList();

        // A reader will take file order for execution order. This keeps that assumption true.
        stagesInRowOrder.Should().BeInAscendingOrder();
    }

    [Fact]
    public void Pre_stage_rows_are_declared_before_every_other_family()
    {
        var families = PipelineContracts.All.Select(contract => contract.Family).ToList();
        var lastPreStage = families.LastIndexOf(PipelineFamily.PreStage);
        var firstOther = families.FindIndex(family => family != PipelineFamily.PreStage);

        // The pre-stage block reads as the pipeline's running order, which only holds while nothing is spliced into it.
        lastPreStage.Should().BeLessThan(firstOther);
    }

    [Fact]
    public void The_run_order_is_the_order_the_enum_declares()
    {
        PipelineContracts.StagesInOrder.Should().Equal(
            PreStage.Guard,
            PreStage.Validator,
            PreStage.Shortcut,
            PreStage.PreHandler);
    }

    [Fact]
    public void Only_validation_collects_failures()
    {
        var collecting = PipelineContracts.All
            .Where(contract => contract.Aggregation == StageAggregation.CollectFailures)
            .Select(contract => contract.Stage)
            .Distinct();

        // The collecting runner builds a decision reporting Invalid, so a second collecting stage would have to decide
        // what it produces before it could reuse that path.
        collecting.Should().Equal(PreStage.Validator);
    }

    [Fact]
    public void Only_pre_stage_rows_name_a_stage()
    {
        foreach (var contract in PipelineContracts.All)
        {
            // A stage on a post-handler row would be read by nothing and would make StageFor answer for a contract that
            // never runs in the pre stage.
            (contract.Stage is not null).Should().Be(
                contract.Family == PipelineFamily.PreStage,
                $"{contract.ContractDefinition.Name} is in the {contract.Family} family");
        }
    }

    [Fact]
    public void Every_contract_is_declared_once()
    {
        // Find indexes rows by contract definition, so a duplicate would silently lose one of the two.
        PipelineContracts.All.Select(contract => contract.ContractDefinition)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_row_names_an_invoker_that_exists_and_can_be_bound()
    {
        foreach (var contract in PipelineContracts.All)
        {
            var invoker = typeof(PipelineDispatch).GetMethod(
                contract.InvokerMethodName,
                BindingFlags.NonPublic | BindingFlags.Static);

            // The name is bound by reflection at registration, so a typo would surface as a null reference on the first
            // mediation of a message using that contract rather than here.
            invoker.Should().NotBeNull($"{contract.ContractDefinition.Name} names invoker {contract.InvokerMethodName}");

            invoker!.GetGenericArguments().Should().HaveCount(
                contract.ContractDefinition.GetGenericArguments().Length,
                $"{contract.InvokerMethodName} is closed over the contract's own type arguments");
        }
    }

    [Fact]
    public void Every_pre_stage_contract_resolves_to_the_stage_dispatch_reports()
    {
        foreach (var contract in PipelineContracts.All.Where(row => row.Stage is not null))
        {
            PipelineDispatch.StageFor(contract.ContractDefinition).Should().Be(contract.Stage!.Value);
        }
    }

    [Fact]
    public void A_contract_that_names_no_stage_falls_back_to_the_pre_handler_stage()
    {
        // Post-handler and completion contracts reach StageFor through the shared descriptor path, and anything that
        // claims no stage runs last rather than being rejected.
        PipelineDispatch.StageFor(typeof(IMessagePostHandler<,>)).Should().Be(PreStage.PreHandler);
        PipelineDispatch.StageFor(typeof(IMessagePreStageHandler)).Should().Be(PreStage.PreHandler);
    }

    [Fact]
    public void Every_closed_contract_in_the_table_builds_a_dispatch()
    {
        foreach (var contract in PipelineContracts.All)
        {
            var closed = Close(contract.ContractDefinition);

            // For returns null for anything it cannot dispatch, and a null dispatch on a descriptor sends the pipeline
            // down the runtime-binding path on every single invocation.
            PipelineDispatch.For(closed).Should().NotBeNull($"{contract.ContractDefinition.Name} is a declared contract");
        }
    }

    [Fact]
    public void A_contract_outside_the_table_builds_no_dispatch()
    {
        PipelineDispatch.For(typeof(IComparable<int>)).Should().BeNull();
    }

    /// <summary>
    ///     Closes an open contract definition over throwaway types so the dispatch factory has something to bind.
    /// </summary>
    /// <param name="definition">The open generic contract from a table row.</param>
    /// <returns>The contract closed over <see cref="TableProbeMessage" /> and, when it takes one, a result type.</returns>
    private static Type Close(Type definition)
    {
        var arity = definition.GetGenericArguments().Length;

        return arity == 1
            ? definition.MakeGenericType(typeof(TableProbeMessage))
            : definition.MakeGenericType(typeof(TableProbeMessage), typeof(string));
    }

    /// <summary>
    ///     A message type that exists only to close the table's contracts over something.
    /// </summary>
    private sealed class TableProbeMessage;
}
