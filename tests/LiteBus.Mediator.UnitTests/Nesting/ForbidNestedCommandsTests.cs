using System.Linq;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Nesting;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Nesting;

/// <summary>
///     Verifies that a command sent from inside another command's handling is denied, while queries, events, and root
///     mediations are unaffected.
/// </summary>
/// <remarks>
///     Each command commits in its own pipeline, so a nested send commits before the outer command finishes. The tests
///     prove the setting denies exactly that shape: a command under a command, including one sent from an event handler
///     under a command, while a nested query, a nested event, a command under only events, and a root mediation all
///     proceed.
/// </remarks>
[Collection("Sequential")]
public sealed class ForbidNestedCommandsTests : LiteBusTestBase
{
    [Fact]
    public async Task A_command_sent_from_inside_another_command_is_denied()
    {
        var recorder = new NestingRecorder();
        var provider = BuildProvider(recorder, new NestingProbeOptions(), forbidNestedCommands: true);
        var outer = new NestingProbeCommand { Action = NestingProbeAction.SendCommand };

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(outer).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<NestedMediationException>().ConfigureAwait(false);
        thrown.Which.InnerMessageType.Should().Be<NestingInnerCommand>();
        thrown.Which.OuterMessageType.Should().Be<NestingProbeCommand>();
        thrown.Which.Code.Should().Be(ForbidNestedCommandsRule.NestedCommand);
        thrown.Which.Message.Should().Contain(nameof(NestingInnerCommand)).And.Contain(nameof(NestingProbeCommand));

        outer.HandlerRan.Should().BeTrue();

        // The inner pipeline never started, so the inner handler recorded nothing.
        recorder.Observed.Select(observation => observation.MessageType).Should().Equal(typeof(NestingProbeCommand));
    }

    [Fact]
    public async Task A_query_sent_from_inside_a_command_is_allowed()
    {
        var recorder = new NestingRecorder();
        var provider = BuildProvider(recorder, new NestingProbeOptions(), forbidNestedCommands: true);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new NestingProbeCommand { Action = NestingProbeAction.SendQuery }).ConfigureAwait(false);

        recorder.Answers.Should().Equal(NestingProbeQueryHandler.Answer);
        recorder.Observed.Select(observation => observation.MessageType)
            .Should().Equal(typeof(NestingProbeCommand), typeof(NestingProbeQuery));
    }

    [Fact]
    public async Task An_event_published_from_inside_a_command_is_allowed()
    {
        var recorder = new NestingRecorder();
        var provider = BuildProvider(recorder, new NestingProbeOptions(), forbidNestedCommands: true);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new NestingProbeCommand { Action = NestingProbeAction.PublishEvent }).ConfigureAwait(false);

        recorder.Observed.Select(observation => observation.MessageType)
            .Should().Equal(typeof(NestingProbeCommand), typeof(NestingProbeEvent));
    }

    [Fact]
    public async Task A_command_sent_from_an_event_handler_under_a_command_is_denied()
    {
        var recorder = new NestingRecorder();
        var options = new NestingProbeOptions { EventAction = NestingEventAction.SendCommand };
        var provider = BuildProvider(recorder, options, forbidNestedCommands: true);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new NestingProbeCommand { Action = NestingProbeAction.PublishEvent }).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<NestedMediationException>().ConfigureAwait(false);
        thrown.Which.InnerMessageType.Should().Be<NestingInnerCommand>();
        thrown.Which.Code.Should().Be(ForbidNestedCommandsRule.NestedCommand);

        // The exception reports the nearest enclosing mediation; the reason names the command ancestor the rule found
        // through the chain.
        thrown.Which.OuterMessageType.Should().Be<NestingProbeEvent>();
        thrown.Which.Reason.Should().Contain(nameof(NestingProbeCommand));

        recorder.Observed.Select(observation => observation.MessageType)
            .Should().Equal(typeof(NestingProbeCommand), typeof(NestingProbeEvent));
    }

    [Fact]
    public async Task A_root_command_mediation_is_unaffected()
    {
        var recorder = new NestingRecorder();
        var provider = BuildProvider(recorder, new NestingProbeOptions(), forbidNestedCommands: true);
        var outer = new NestingProbeCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(outer).ConfigureAwait(false);

        outer.HandlerRan.Should().BeTrue();
        recorder.Observed.Should().ContainSingle();
    }

    [Fact]
    public async Task Without_the_setting_a_nested_command_is_allowed()
    {
        var recorder = new NestingRecorder();
        var provider = BuildProvider(recorder, new NestingProbeOptions(), forbidNestedCommands: false);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new NestingProbeCommand { Action = NestingProbeAction.SendCommand }).ConfigureAwait(false);

        recorder.Observed.Select(observation => observation.MessageType)
            .Should().Equal(typeof(NestingProbeCommand), typeof(NestingInnerCommand));
    }

    [Fact]
    public async Task A_command_sent_from_an_event_handler_at_the_root_is_allowed()
    {
        var recorder = new NestingRecorder();
        var options = new NestingProbeOptions { EventAction = NestingEventAction.SendCommand };
        var provider = BuildProvider(recorder, options, forbidNestedCommands: true);

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new NestingProbeEvent()).ConfigureAwait(false);

        recorder.Observed.Select(observation => observation.MessageType)
            .Should().Equal(typeof(NestingProbeEvent), typeof(NestingInnerCommand));
    }

    /// <summary>
    ///     Builds a provider registering the probe messages and handlers on the command, query, and event axes.
    /// </summary>
    /// <param name="recorder">The recorder shared with the handlers.</param>
    /// <param name="options">The options shared with the event handler.</param>
    /// <param name="forbidNestedCommands">Whether the command builder forbids nested commands.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(
        NestingRecorder recorder,
        NestingProbeOptions options,
        bool forbidNestedCommands)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddSingleton(options);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<NestingProbeCommand>();
                    builder.Register<NestingProbeCommandHandler>();
                    builder.Register<NestingInnerCommand>();
                    builder.Register<NestingInnerCommandHandler>();

                    if (forbidNestedCommands)
                    {
                        builder.ForbidNestedCommands();
                    }
                });

                registry.AddQueries(builder =>
                {
                    builder.Register<NestingProbeQuery>();
                    builder.Register<NestingProbeQueryHandler>();
                });

                registry.AddEvents(builder =>
                {
                    builder.Register<NestingProbeEvent>();
                    builder.Register<NestingProbeEventHandler>();
                });
            })
            .BuildServiceProvider();
    }
}
