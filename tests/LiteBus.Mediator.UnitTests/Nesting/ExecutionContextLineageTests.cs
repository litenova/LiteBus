using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Queries;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Nesting;

/// <summary>
///     Verifies the execution context lineage every mediation records: its message type and its enclosing mediation.
/// </summary>
/// <remarks>
///     Lineage is recorded for every mediation whether or not nesting rules are registered. Nesting rules and
///     authorization policies read it; these tests prove the values they read are the ones the mediations carried.
/// </remarks>
[Collection("Sequential")]
public sealed class ExecutionContextLineageTests : LiteBusTestBase
{
    [Fact]
    public async Task A_root_mediation_has_no_parent_and_names_its_message()
    {
        var recorder = new NestingRecorder();
        var provider = BuildProvider(recorder, new NestingProbeOptions(), forbidNestedCommands: true);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new NestingProbeCommand()).ConfigureAwait(false);

        var observed = recorder.Observed.Should().ContainSingle().Which;
        observed.MessageType.Should().Be<NestingProbeCommand>();
        observed.Ancestors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_nested_mediation_records_its_parent()
    {
        var recorder = new NestingRecorder();
        var provider = BuildProvider(recorder, new NestingProbeOptions(), forbidNestedCommands: true);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new NestingProbeCommand { Action = NestingProbeAction.SendQuery }).ConfigureAwait(false);

        recorder.Observed.Should().HaveCount(2);
        var observed = recorder.Observed[1];
        observed.MessageType.Should().Be<NestingProbeQuery>();
        observed.Ancestors.Should().Equal(typeof(NestingProbeCommand));
    }

    [Fact]
    public async Task A_third_level_mediation_sees_the_whole_chain()
    {
        var recorder = new NestingRecorder();
        var options = new NestingProbeOptions { EventAction = NestingEventAction.SendQuery };
        var provider = BuildProvider(recorder, options, forbidNestedCommands: true);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new NestingProbeCommand { Action = NestingProbeAction.PublishEvent }).ConfigureAwait(false);

        recorder.Observed.Should().HaveCount(3);
        var observed = recorder.Observed[2];
        observed.MessageType.Should().Be<NestingProbeQuery>();
        observed.Ancestors.Should().Equal(typeof(NestingProbeEvent), typeof(NestingProbeCommand));
    }

    [Fact]
    public async Task Lineage_is_recorded_without_nesting_rules()
    {
        var recorder = new NestingRecorder();
        var provider = BuildProvider(recorder, new NestingProbeOptions(), forbidNestedCommands: false);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new NestingProbeCommand { Action = NestingProbeAction.SendQuery }).ConfigureAwait(false);

        recorder.Observed.Should().HaveCount(2);
        var observed = recorder.Observed[1];
        observed.MessageType.Should().Be<NestingProbeQuery>();
        observed.Ancestors.Should().Equal(typeof(NestingProbeCommand));
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
