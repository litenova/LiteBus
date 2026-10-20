using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.Testing;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Verifies the recording mediator doubles LiteBus ships, which an application injects in place of a real mediator
///     to assert that a component sent what it was supposed to send.
/// </summary>
/// <remarks>
///     They are shipped API, so their behavior is a contract: what they record, what they return, and what
///     <c>Clear</c> resets. A double that quietly stops recording is worse than no double, because the test it
///     supports keeps passing.
/// </remarks>
public sealed class TestMediatorDoubleTests
{
    [Fact]
    public async Task The_command_double_records_what_was_sent_and_returns_a_default_result()
    {
        var mediator = new TestCommandMediator();
        ICommandMediator contract = mediator;

        await contract.SendAsync(new DoubledCommand()).ConfigureAwait(false);
        var value = await contract.SendAsync(new DoubledResultCommand()).ConfigureAwait(false);

        mediator.Commands.Should().HaveCount(2);
        value.Should().BeNull();
    }

    [Fact]
    public async Task The_command_double_reports_a_succeeded_result_from_the_try_methods()
    {
        var mediator = new TestCommandMediator();
        ICommandMediator contract = mediator;

        var plain = await contract.TrySendAsync(new DoubledCommand()).ConfigureAwait(false);
        var typed = await contract.TrySendAsync(new DoubledResultCommand()).ConfigureAwait(false);

        plain.IsSuccess.Should().BeTrue();
        typed.IsSuccess.Should().BeTrue();
        mediator.Commands.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_command_double_records_an_evaluation_apart_from_a_send()
    {
        var mediator = new TestCommandMediator();
        ICommandMediator contract = mediator;

        var decision = await contract.EvaluateAsync(new DoubledCommand()).ConfigureAwait(false);

        // Evaluating asks whether a command may happen and does not perform it, so asserting that a control
        // evaluated one is a different assertion from asserting it sent one.
        decision.IsAllowed.Should().BeTrue();
        mediator.Evaluated.Should().ContainSingle();
        mediator.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task Clearing_the_command_double_resets_both_records()
    {
        var mediator = new TestCommandMediator();
        ICommandMediator contract = mediator;

        await contract.SendAsync(new DoubledCommand()).ConfigureAwait(false);
        await contract.EvaluateAsync(new DoubledCommand()).ConfigureAwait(false);

        mediator.Clear();

        mediator.Commands.Should().BeEmpty();
        mediator.Evaluated.Should().BeEmpty();
    }

    [Fact]
    public async Task The_query_double_returns_the_next_result_it_was_given()
    {
        var mediator = new TestQueryMediator { NextResult = "seeded" };
        IQueryMediator contract = mediator;

        var value = await contract.QueryAsync(new DoubledQuery()).ConfigureAwait(false);
        var tried = await contract.TryQueryAsync(new DoubledQuery()).ConfigureAwait(false);

        value.Should().Be("seeded");
        tried.IsSuccess.Should().BeTrue();
        tried.Value.Should().Be("seeded");
        mediator.Queries.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_query_double_streams_the_next_result_when_it_matches_the_element_type()
    {
        var mediator = new TestQueryMediator { NextResult = "streamed" };
        IQueryMediator contract = mediator;

        var streamed = new List<string>();

        await foreach (var item in contract.StreamAsync(new DoubledStreamQuery()).ConfigureAwait(false))
        {
            streamed.Add(item);
        }

        streamed.Should().Equal("streamed");
        mediator.Queries.Should().ContainSingle();
    }

    [Fact]
    public async Task The_query_double_records_an_evaluation_apart_from_a_query()
    {
        var mediator = new TestQueryMediator();
        IQueryMediator contract = mediator;

        var decision = await contract.EvaluateAsync(new DoubledQuery()).ConfigureAwait(false);

        decision.IsAllowed.Should().BeTrue();
        mediator.Evaluated.Should().ContainSingle();
        mediator.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task Clearing_the_query_double_resets_the_records_and_the_next_result()
    {
        var mediator = new TestQueryMediator { NextResult = "seeded" };
        IQueryMediator contract = mediator;

        await contract.QueryAsync(new DoubledQuery()).ConfigureAwait(false);
        await contract.EvaluateAsync(new DoubledQuery()).ConfigureAwait(false);

        mediator.Clear();

        mediator.Queries.Should().BeEmpty();
        mediator.Evaluated.Should().BeEmpty();
        mediator.NextResult.Should().BeNull();
    }

    [Fact]
    public async Task The_event_double_records_what_was_published()
    {
        var mediator = new TestEventMediator();
        IEventMediator contract = mediator;

        await contract.PublishAsync(new DoubledEvent()).ConfigureAwait(false);

        mediator.Events.Should().ContainSingle();

        mediator.Clear();
        mediator.Events.Should().BeEmpty();
    }

    [Fact]
    public void The_doubles_reject_a_null_message()
    {
        var commands = new TestCommandMediator();
        var queries = new TestQueryMediator();

        var sendNull = async () => await commands.SendAsync(null!).ConfigureAwait(false);
        var evaluateNull = async () => await commands.EvaluateAsync(null!).ConfigureAwait(false);
        var queryNull = async () => await queries.QueryAsync<string>(null!).ConfigureAwait(false);

        sendNull.Should().ThrowAsync<ArgumentNullException>();
        evaluateNull.Should().ThrowAsync<ArgumentNullException>();
        queryNull.Should().ThrowAsync<ArgumentNullException>();
    }
}

/// <summary>
///     A command with no result, for the recording double.
/// </summary>
internal sealed class DoubledCommand : ICommand;

/// <summary>
///     A command with a result, for the recording double.
/// </summary>
internal sealed class DoubledResultCommand : ICommand<string>;

/// <summary>
///     A query, for the recording double.
/// </summary>
internal sealed class DoubledQuery : IQuery<string>;

/// <summary>
///     A stream query, for the recording double.
/// </summary>
internal sealed class DoubledStreamQuery : IStreamQuery<string>;

/// <summary>
///     An event, for the recording double.
/// </summary>
internal sealed class DoubledEvent : IEvent;
