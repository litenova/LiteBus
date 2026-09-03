using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Mediator.UnitTests.Completion;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Verifies that the registry can say what runs for one message, in the order it runs.
/// </summary>
/// <remarks>
///     With a hundred messages, open generic guards, an audit writer and a commit, the honest answer to "what runs for
///     this command" was previously to read the registry in a debugger. This is the same traversal a command-line tool
///     or a generated plan would front, so those stay presentation rather than second implementations that can
///     disagree with the runtime.
/// </remarks>
[Collection("Sequential")]
public sealed class PipelineExplainerTests : LiteBusTestBase
{
    [Fact]
    public void The_plan_lists_every_stage_in_the_order_it_runs()
    {
        var plan = Explain(typeof(SteeredCommand));

        plan.Steps.Select(step => step.Stage)
            .Should().Equal("guard", "validator", "shortcut", "prehandler", "main");
    }

    [Fact]
    public void The_plan_names_the_declared_result_type()
    {
        Explain(typeof(SteeredResultCommand)).MessageResultType.Should().Be<string>();
        Explain(typeof(SteeredCommand)).MessageResultType.Should().BeNull();
    }

    [Fact]
    public void The_plan_marks_a_handler_closed_from_an_open_generic()
    {
        var plan = Explain(typeof(SteeredCommand));

        var guard = plan.Steps.Should().ContainSingle(step => step.Stage == "guard").Subject;

        // The registration a reviewer cannot see in the composition code, named in the plan.
        guard.IsClosedOpenGeneric.Should().BeTrue();
        guard.HandlerType.Name.Should().StartWith("SteeredGuard");
    }

    [Fact]
    public void The_plan_reports_the_priority_that_orders_each_step()
    {
        var plan = Explain(typeof(SteeredCommand));

        plan.Steps.Should().AllSatisfy(step => step.Priority.Should().Be(HandlerPriorities.Default));
    }

    [Fact]
    public void Completion_handlers_are_ordered_by_priority_across_the_direct_and_indirect_split()
    {
        var provider = new ServiceCollection()
            .AddSingleton(new CompletionRecorder())
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<CompletionCommand>();
                    builder.Register<CompletionCommandHandler>();

                    // Registered commit-first, and globally before directly, so only priority can produce the order.
                    builder.Register<UnitOfWorkCompletionHandler>();
                    builder.Register<GlobalCompletionHandler>();
                    builder.Register<InfrastructureCompletionHandler>();
                });
            })
            .BuildServiceProvider();

        var plan = provider.GetRequiredService<IMessageReader>().Explain(typeof(CompletionCommand));

        plan.Steps.Where(step => step.Stage == "completion")
            .Select(step => step.HandlerType.Name)
            .Should().Equal(
                nameof(GlobalCompletionHandler),
                nameof(InfrastructureCompletionHandler),
                nameof(UnitOfWorkCompletionHandler));
    }

    [Fact]
    public void A_message_nothing_is_registered_for_produces_an_empty_plan_rather_than_a_failure()
    {
        var plan = Explain(typeof(UnregisteredCommand));

        plan.Steps.Should().BeEmpty();
        plan.ToString().Should().Contain("nothing is registered for this message");
    }

    [Fact]
    public void The_rendered_plan_reads_as_a_block_naming_the_message_and_its_steps()
    {
        var rendered = Explain(typeof(SteeredResultCommand)).ToString();

        rendered.Should().StartWith("SteeredResultCommand -> String");
        rendered.Should().Contain("guard");
        rendered.Should().Contain("main");
    }

    /// <summary>
    ///     Builds the standard steered provider and explains one message.
    /// </summary>
    /// <param name="messageType">The message to explain.</param>
    /// <returns>The plan.</returns>
    private static MessagePipelinePlan Explain(Type messageType)
    {
        var provider = new ServiceCollection()
            .AddSingleton(new StageActivityRecorder())
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<SteeredCommand>();
                    builder.Register<SteeredCommandHandler>();
                    builder.Register<SteeredResultCommand>();
                    builder.Register<SteeredResultCommandHandler>();
                    builder.Register(typeof(SteeredGuard<>));
                    builder.Register(typeof(SteeredValidator<>));
                    builder.Register<SteeredShortcut>();
                    builder.Register<SteeredPreHandler>();
                });
            })
            .BuildServiceProvider();

        return provider.GetRequiredService<IMessageReader>().Explain(messageType);
    }
}
