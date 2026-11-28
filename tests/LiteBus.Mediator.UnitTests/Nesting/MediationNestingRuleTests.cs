using System.Linq;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Nesting;

/// <summary>
///     Verifies an application nesting rule registered in the container judges the nesting it names.
/// </summary>
/// <remarks>
///     The built-in command rule is one registration of the port. These tests prove the port itself: a rule the
///     application writes and registers participates in the same evaluation, and nestings it allows proceed.
/// </remarks>
[Collection("Sequential")]
public sealed class MediationNestingRuleTests : LiteBusTestBase
{
    [Fact]
    public async Task An_application_rule_denies_the_nesting_it_names()
    {
        var recorder = new NestingRecorder();
        var provider = BuildProvider(recorder);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new NestingProbeCommand { Action = NestingProbeAction.SendQuery }).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<NestedMediationException>().ConfigureAwait(false);
        thrown.Which.InnerMessageType.Should().Be<NestingProbeQuery>();
        thrown.Which.OuterMessageType.Should().Be<NestingProbeCommand>();
        thrown.Which.Code.Should().Be(DenyNestedQueriesRule.NestedQuery);
    }

    [Fact]
    public async Task An_application_rule_leaves_other_nestings_allowed()
    {
        var recorder = new NestingRecorder();
        var provider = BuildProvider(recorder);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new NestingProbeCommand { Action = NestingProbeAction.SendCommand }).ConfigureAwait(false);

        recorder.Observed.Select(observation => observation.MessageType)
            .Should().Equal(typeof(NestingProbeCommand), typeof(NestingInnerCommand));
    }

    /// <summary>
    ///     Builds a provider registering the probe messages and handlers with the application rule wired directly.
    /// </summary>
    /// <param name="recorder">The recorder shared with the handlers.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(NestingRecorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddSingleton(new NestingProbeOptions());
        services.AddSingleton<IMediationNestingRule>(new DenyNestedQueriesRule());

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
