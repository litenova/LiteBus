using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Nesting;

/// <summary>
///     Verifies the nesting context walks its enclosing mediations from the direct parent to the root.
/// </summary>
public sealed class MediationNestingContextTests
{
    [Fact]
    public void Ancestors_walks_from_the_direct_parent_to_the_root()
    {
        var root = new StubExecutionContext(typeof(NestingProbeCommand));
        var middle = new StubExecutionContext(typeof(NestingProbeEvent), root);
        var direct = new StubExecutionContext(typeof(NestingProbeQuery), middle);

        var context = new MediationNestingContext(direct, typeof(NestingInnerCommand));

        context.Ancestors().Should().Equal(direct, middle, root);
    }

    [Fact]
    public void Ancestors_of_a_root_parent_yields_only_it()
    {
        var parent = new StubExecutionContext(typeof(NestingProbeCommand));

        var context = new MediationNestingContext(parent, typeof(NestingInnerCommand));

        context.Ancestors().Should().Equal(parent);
    }
}
