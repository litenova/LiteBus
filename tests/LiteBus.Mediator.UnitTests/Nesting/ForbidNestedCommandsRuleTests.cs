using LiteBus.Commands.Nesting;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Nesting;

/// <summary>
///     Verifies the built-in nesting rule denies a command under a command and allows every other shape.
/// </summary>
public sealed class ForbidNestedCommandsRuleTests
{
    [Fact]
    public void The_denial_carries_the_documented_code()
    {
        ForbidNestedCommandsRule.NestedCommand.Should().Be("commands.nested-command");
    }

    [Fact]
    public void A_command_under_a_command_is_denied()
    {
        var rule = new ForbidNestedCommandsRule();
        var parent = new StubExecutionContext(typeof(NestingProbeCommand));

        var verdict = rule.Evaluate(new MediationNestingContext(parent, typeof(NestingInnerCommand)));

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(ForbidNestedCommandsRule.NestedCommand);
        verdict.Reason.Should().Contain(nameof(NestingInnerCommand)).And.Contain(nameof(NestingProbeCommand));
    }

    [Fact]
    public void A_query_under_a_command_is_allowed()
    {
        var rule = new ForbidNestedCommandsRule();
        var parent = new StubExecutionContext(typeof(NestingProbeCommand));

        var verdict = rule.Evaluate(new MediationNestingContext(parent, typeof(NestingProbeQuery)));

        verdict.Should().Be(Verdict.Allow);
    }

    [Fact]
    public void An_event_under_a_command_is_allowed()
    {
        var rule = new ForbidNestedCommandsRule();
        var parent = new StubExecutionContext(typeof(NestingProbeCommand));

        var verdict = rule.Evaluate(new MediationNestingContext(parent, typeof(NestingProbeEvent)));

        verdict.Should().Be(Verdict.Allow);
    }

    [Fact]
    public void A_command_under_only_events_is_allowed()
    {
        var rule = new ForbidNestedCommandsRule();
        var root = new StubExecutionContext(typeof(NestingProbeEvent));
        var parent = new StubExecutionContext(typeof(NestingProbeEvent), root);

        var verdict = rule.Evaluate(new MediationNestingContext(parent, typeof(NestingInnerCommand)));

        verdict.Should().Be(Verdict.Allow);
    }

    [Fact]
    public void A_command_finds_a_command_ancestor_through_an_event()
    {
        var rule = new ForbidNestedCommandsRule();
        var root = new StubExecutionContext(typeof(NestingProbeCommand));
        var parent = new StubExecutionContext(typeof(NestingProbeEvent), root);

        var verdict = rule.Evaluate(new MediationNestingContext(parent, typeof(NestingInnerCommand)));

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(ForbidNestedCommandsRule.NestedCommand);
        verdict.Reason.Should().Contain(nameof(NestingProbeCommand));
    }

    [Fact]
    public void Ancestors_without_a_message_type_are_skipped()
    {
        var rule = new ForbidNestedCommandsRule();
        var root = new StubExecutionContext(typeof(NestingProbeCommand));
        var parent = new StubExecutionContext(messageType: null, parent: root);

        var verdict = rule.Evaluate(new MediationNestingContext(parent, typeof(NestingInnerCommand)));

        verdict.IsDenied.Should().BeTrue();
    }
}
