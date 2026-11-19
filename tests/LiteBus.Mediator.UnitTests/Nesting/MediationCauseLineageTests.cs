using System.Collections.Generic;
using LiteBus.Messaging;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Nesting;
using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using ExecutionContext = LiteBus.Messaging.Contexts.Execution.ExecutionContext;

namespace LiteBus.Mediator.UnitTests.Nesting;

/// <summary>
///     Verifies a mediation carrying a cause starts as a root with that cause and no parent.
/// </summary>
/// <remarks>
///     A boundary starts a root mediation by setting <see cref="MediationCause" /> on the mediation settings. The
///     mediator copies it into the created execution context and leaves <see cref="IExecutionContext.Parent" /> as
///     <see langword="null" />, even inside an ambient scope, so nesting rules and identity inheritance treat the
///     mediation as a root.
/// </remarks>
[Collection("Sequential")]
public sealed class MediationCauseLineageTests : LiteBusTestBase
{
    [Fact]
    public void An_execution_context_carries_its_cause()
    {
        var cause = new MediationCause("correlation-1", "causation-1", MediationCauseSource.Outbox);

        var context = new ExecutionContext(
            [],
            new Dictionary<string, object>(),
            TimeProvider.System,
            ExecutionIdentity.Anonymous,
            CancellationToken.None,
            typeof(NestingProbeCommand),
            parent: null,
            cause: cause);

        context.Cause.Should().Be(cause);
        context.Parent.Should().BeNull();
        context.MessageType.Should().Be<NestingProbeCommand>();
    }

    [Fact]
    public void An_execution_context_without_a_cause_has_no_cause()
    {
        var context = new ExecutionContext(
            [],
            new Dictionary<string, object>(),
            TimeProvider.System,
            ExecutionIdentity.Anonymous,
            CancellationToken.None,
            typeof(NestingProbeCommand));

        context.Cause.Should().BeNull();
    }

    [Fact]
    public async Task A_command_with_a_cause_runs_as_a_root_with_that_cause()
    {
        var recorder = new CauseRecorder();
        var provider = BuildProvider(recorder);

        var cause = new MediationCause("correlation-9", "causation-9", MediationCauseSource.Outbox);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CauseProbeCommand(), new CommandMediationSettings { Cause = cause })
            .ConfigureAwait(false);

        var observed = recorder.Observed.Should().ContainSingle().Subject;
        observed.Cause.Should().Be(cause);
        observed.Parent.Should().BeNull();
        observed.MessageType.Should().Be<CauseProbeCommand>();
    }

    [Fact]
    public async Task A_cause_inside_an_ambient_scope_still_runs_as_a_root()
    {
        var recorder = new CauseRecorder();
        var provider = BuildProvider(recorder);

        var outer = new StubExecutionContext(typeof(NestingProbeCommand));

        using (AmbientExecutionContext.CreateScope(outer))
        {
            var cause = new MediationCause("correlation-7", "causation-7", MediationCauseSource.ImmediateDispatch);

            await provider.GetRequiredService<ICommandMediator>()
                .SendAsync(new CauseProbeCommand(), new CommandMediationSettings { Cause = cause })
                .ConfigureAwait(false);
        }

        var observed = recorder.Observed.Should().ContainSingle().Subject;
        observed.Cause.Should().NotBeNull();
        observed.Cause!.Source.Should().Be(MediationCauseSource.ImmediateDispatch);
        observed.Parent.Should().BeNull();
    }

    [Fact]
    public async Task A_cause_does_not_inherit_the_enclosing_identity()
    {
        var recorder = new CauseRecorder();
        var provider = BuildProvider(recorder);

        var outer = new StubExecutionContext(typeof(NestingProbeCommand));

        using (AmbientExecutionContext.CreateScope(outer))
        {
            var cause = new MediationCause("correlation-3", "causation-3", MediationCauseSource.Outbox);
            var identity = ExecutionIdentity.ForTenant("tenant-cause");

            await provider.GetRequiredService<ICommandMediator>()
                .SendAsync(new CauseProbeCommand(), new CommandMediationSettings { Cause = cause, Identity = identity })
                .ConfigureAwait(false);
        }

        var observed = recorder.Observed.Should().ContainSingle().Subject;
        observed.Identity.TenantId.Should().Be("tenant-cause");
        observed.Parent.Should().BeNull();
    }

    [Fact]
    public async Task A_command_with_a_cause_under_forbid_nested_commands_is_allowed()
    {
        var recorder = new CauseRecorder();
        var provider = BuildProvider(recorder, forbidNestedCommands: true);

        var outer = new StubExecutionContext(typeof(NestingProbeCommand));

        using (AmbientExecutionContext.CreateScope(outer))
        {
            var cause = new MediationCause("correlation-5", "causation-5", MediationCauseSource.Outbox);

            await provider.GetRequiredService<ICommandMediator>()
                .SendAsync(new CauseProbeCommand(), new CommandMediationSettings { Cause = cause })
                .ConfigureAwait(false);
        }

        recorder.Observed.Should().ContainSingle();
    }

    [Fact]
    public async Task A_nested_command_without_a_cause_is_still_denied()
    {
        var recorder = new NestingRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddSingleton(new NestingProbeOptions());

        var provider = services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<NestingProbeCommand>();
                    builder.Register<NestingProbeCommandHandler>();
                    builder.Register<NestingInnerCommand>();
                    builder.Register<NestingInnerCommandHandler>();
                    builder.ForbidNestedCommands();
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

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new NestingProbeCommand { Action = NestingProbeAction.SendCommand })
            .ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<NestedMediationException>().ConfigureAwait(false);
        thrown.Which.Code.Should().Be(ForbidNestedCommandsRule.NestedCommand);
    }

    /// <summary>
    ///     Builds a provider registering the cause probe command and its handler.
    /// </summary>
    /// <param name="recorder">The recorder shared with the handler.</param>
    /// <param name="forbidNestedCommands">Whether the command builder forbids nested commands.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(CauseRecorder recorder, bool forbidNestedCommands = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<CauseProbeCommand>();
                    builder.Register<CauseProbeCommandHandler>();

                    if (forbidNestedCommands)
                    {
                        builder.ForbidNestedCommands();
                    }
                });
            })
            .BuildServiceProvider();
    }
}
