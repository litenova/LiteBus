using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Completion;

/// <summary>
///     Guards the invariants the pre-stage table relies on but cannot express in its own shape.
/// </summary>
/// <remarks>
///     The table is the one place a pre-stage role is declared, and four call sites read from it. These assertions cover
///     the assumptions those readers make, so a wrong row fails here rather than as strange behavior somewhere else.
/// </remarks>
public sealed class PreStageTableTests
{
    [Fact]
    public void Every_declared_stage_has_at_least_one_contract()
    {
        var declared = PreStages.All.Select(definition => definition.Stage).Distinct();

        // A stage with no contract can never run, so it would be a member nothing could ever land in.
        declared.Should().BeEquivalentTo(PreStages.InOrder);
    }

    [Fact]
    public void Rows_sharing_a_stage_agree_on_how_it_aggregates()
    {
        foreach (var group in PreStages.All.GroupBy(definition => definition.Stage))
        {
            // AggregationFor reads the first row for a stage. Two rows disagreeing would make which one wins depend on
            // declaration order, which is exactly the class of accident the table exists to remove.
            group.Select(definition => definition.Aggregation).Distinct().Should().ContainSingle(
                $"stage {group.Key} must aggregate one way");
        }
    }

    [Fact]
    public void Rows_are_declared_in_the_order_their_stages_run()
    {
        var stagesInRowOrder = PreStages.All.Select(definition => (int) definition.Stage).ToList();

        // A reader will take file order for execution order. This keeps that assumption true.
        stagesInRowOrder.Should().BeInAscendingOrder();
    }

    [Fact]
    public void The_run_order_is_the_order_the_enum_declares()
    {
        PreStages.InOrder.Should().Equal(
            PipelineStage.Guard,
            PipelineStage.Validator,
            PipelineStage.Shortcut,
            PipelineStage.PreHandler);
    }

    [Fact]
    public void Only_validation_collects_failures()
    {
        var collecting = PreStages.All
            .Where(definition => definition.Aggregation == StageAggregation.CollectFailures)
            .Select(definition => definition.Stage)
            .Distinct();

        // The collecting runner builds a stop reporting Invalid, so a second collecting stage would have to decide what
        // it produces before it could reuse that path.
        collecting.Should().Equal(PipelineStage.Validator);
    }

    [Fact]
    public void Every_contract_resolves_to_the_stage_dispatch_reports()
    {
        foreach (var definition in PreStages.All)
        {
            PipelineDispatch.StageFor(definition.ContractDefinition).Should().Be(definition.Stage);
        }
    }

    [Fact]
    public void A_contract_outside_the_table_falls_back_to_the_pre_handler_stage()
    {
        // Post-handler and completion contracts reach StageFor through the shared descriptor path, and anything the
        // table does not claim runs last rather than being rejected.
        PipelineDispatch.StageFor(typeof(IMessagePostHandler<,>)).Should().Be(PipelineStage.PreHandler);
        PipelineDispatch.StageFor(typeof(IMessagePreStageHandler)).Should().Be(PipelineStage.PreHandler);
    }
}
