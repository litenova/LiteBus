using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Testing;

namespace LiteBus.Mediator.UnitTests.Registry;

/// <summary>
///     Verifies that the pipeline plan reports the result type the registry itself would bind.
/// </summary>
public sealed class MessagePipelineExplainerResultTypeTests : LiteBusTestBase
{
    [Fact]
    public void The_plan_reports_no_result_type_for_a_message_declaring_two_result_types()
    {
        var registry = new MessageRegistry();
        registry.Register(typeof(TwoResultMessage));

        registry.Explain(typeof(TwoResultMessage)).MessageResultType.Should().BeNull();
    }

    [Fact]
    public void The_plan_reports_the_declared_result_type_for_a_message_declaring_one()
    {
        var registry = new MessageRegistry();
        registry.Register(typeof(SingleResultMessage));

        registry.Explain(typeof(SingleResultMessage)).MessageResultType.Should().Be(typeof(string));
    }

    private sealed class TwoResultMessage : IProducesResult<string>, IProducesResult<int>;

    private sealed class SingleResultMessage : IProducesResult<string>;
}
