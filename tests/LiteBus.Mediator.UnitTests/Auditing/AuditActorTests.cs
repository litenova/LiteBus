using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Audit;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Auditing;

/// <summary>
///     Verifies that an audit record says who performed the action, on every path including a denial, and that the
///     whole feature is configured through one call.
/// </summary>
/// <remarks>
///     Without an actor on the record, every consumer builds the same workaround: a scoped holder plus a guard that
///     allows everything and exists only to populate it before anything can deny. Resolving at the completion stage is
///     what removes the need for that guard.
/// </remarks>
[Collection("Sequential")]
public sealed class AuditActorTests : LiteBusTestBase
{
    [Fact]
    public async Task A_record_names_the_actor_the_resolver_read_from_the_message()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CloseAccountCommand { ActingAccountId = "acct-7" }).ConfigureAwait(false);

        var actor = trail.Records.Should().ContainSingle().Which.Actor;
        actor.Should().NotBeNull();
        actor!.Id.Should().Be("acct-7");
        actor.Kind.Should().Be(AuditActor.UserKind);
        actor.DisplayName.Should().Be("Acting Person");
    }

    [Fact]
    public async Task A_denied_command_is_still_attributed()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CloseAccountCommand { ActingAccountId = "acct-9", ShouldDeny = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);

        // The case the trail exists for. A pre-stage handler could not do this: a guard that denies stops the pipeline
        // before any pre-handler runs, which is exactly when "who tried" matters most.
        var record = trail.Records.Should().ContainSingle().Which;
        record.Outcome.Should().Be(AuditOutcome.Denied);
        record.Actor!.Id.Should().Be("acct-9");
        record.FailureCode.Should().Be("NOT_PERMITTED");
    }

    [Fact]
    public async Task A_command_with_no_acting_account_is_attributed_to_the_process()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ExpireSessionsCommand()).ConfigureAwait(false);

        // A scheduled job and an unattributed action are different answers, and a query has to tell them apart.
        var actor = trail.Records.Should().ContainSingle().Which.Actor;
        actor!.Kind.Should().Be(AuditActor.SystemKind);
        actor.Id.Should().Be("scheduled-worker");
    }

    [Fact]
    public async Task The_handler_overrides_the_actor_the_resolver_resolved()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CloseAccountCommand { HandlerAttributes = true }).ConfigureAwait(false);

        // A resolver states the rule for every message; a handler that pushed an actor knows something the rule does
        // not, so overriding is the only precedence that makes the call worth having.
        trail.Records.Should().ContainSingle().Which.Actor!.Id.Should().Be("acct-override");
    }

    [Fact]
    public async Task A_resolver_that_establishes_nothing_leaves_the_actor_absent()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail, resolver: typeof(UnattributedAuditActorResolver));

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ExpireSessionsCommand()).ConfigureAwait(false);

        // Null is a real answer: nothing recorded who acted, which a review should be able to find. Inventing an
        // identifier here would put a fabrication into evidence.
        trail.Records.Should().ContainSingle().Which.Actor.Should().BeNull();
    }

    [Fact]
    public async Task Auditing_with_no_resolver_still_records_and_the_probe_reports_the_gap()
    {
        var trail = new RecordingAuditTrail();

        var provider = new ServiceCollection()
            .AddSingleton<IAuditTrail>(trail)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.AddAuditing(auditing => auditing.ForCommands()));
                registry.AddCommands(builder =>
                {
                    builder.Register<ExpireSessionsCommand>();
                    builder.Register<ExpireSessionsCommandHandler>();
                });
            })
            .BuildServiceProvider();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ExpireSessionsCommand()).ConfigureAwait(false);

        trail.Records.Should().ContainSingle().Which.Actor.Should().BeNull();

        var diagnostics = await DiagnosticCheckRunner
            .RunAsync(provider.GetRequiredService<LiteBusHostManifest>(), provider, failHealthWhenNoProbes: true)
            .ConfigureAwait(false);

        var audit = diagnostics.Probes
            .Should().ContainSingle(probe => probe.Name == AuditTrailDiagnosticCheck.CheckName).Subject;

        // Degraded, not unhealthy: a trail with no actor still records what happened, so it is worth writing.
        audit.Status.Should().Be(DiagnosticStatus.Degraded);
        audit.Data!["actorResolverRegistered"].Should().Be(false);
    }

    [Fact]
    public async Task An_audited_event_writes_one_record_per_publish()
    {
        var trail = new RecordingAuditTrail();

        var provider = new ServiceCollection()
            .AddSingleton<IAuditTrail>(trail)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.AddAuditing(auditing => auditing
                    .UseActorResolver<TestAuditActorResolver>()
                    .ForEvents()));

                registry.AddEvents(builder =>
                {
                    builder.Register<AccountClosedEvent>();
                    builder.Register<NotifyOnAccountClosed>();
                    builder.Register<ArchiveOnAccountClosed>();
                });
            })
            .BuildServiceProvider();

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new AccountClosedEvent()).ConfigureAwait(false);

        // Two reactions ran. The mediation is the unit being audited, so a record per subscriber would turn one
        // domain fact into as many entries as there happen to be handlers.
        var record = trail.Records.Should().ContainSingle().Which;
        record.Action.Should().Be("accounts.account-closed");
        record.Outcome.Should().Be(AuditOutcome.Succeeded);
        record.Actor!.Id.Should().Be("account-closed-reaction");
    }

    [Fact]
    public void AddAuditing_covers_every_axis_in_one_call()
    {
        var trail = new RecordingAuditTrail();

        var provider = new ServiceCollection()
            .AddSingleton<IAuditTrail>(trail)
            .AddLiteBus(registry =>
            {
                // One decision, rather than a trail here and a switch on each axis builder.
                registry.AddMessaging(messaging => messaging.AddAuditing(auditing => auditing
                    .UseActorResolver<TestAuditActorResolver>()
                    .ForAllAxes()));

                registry.AddCommands(builder =>
                {
                    builder.Register<ExpireSessionsCommand>();
                    builder.Register<ExpireSessionsCommandHandler>();
                });

                registry.AddEvents(builder =>
                {
                    builder.Register<AccountClosedEvent>();
                    builder.Register<NotifyOnAccountClosed>();
                });
            })
            .BuildServiceProvider();

        // The probe is registered by whichever axis was enabled, so its presence proves the axes read the selection.
        provider.GetRequiredService<LiteBusHostManifest>().DiagnosticChecks
            .Should().Contain(descriptor => descriptor.ImplementationType == typeof(AuditTrailDiagnosticCheck));
    }

    [Fact]
    public void AddAuditing_with_no_axis_selected_is_reported_at_composition()
    {
        var act = () => new ServiceCollection()
            .AddSingleton<IAuditTrail>(new RecordingAuditTrail())
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.AddAuditing(auditing =>
                    auditing.UseActorResolver<TestAuditActorResolver>()));

                registry.AddCommands(builder =>
                {
                    builder.Register<ExpireSessionsCommand>();
                    builder.Register<ExpireSessionsCommandHandler>();
                });
            });

        // No probe can report this at runtime: nothing is ever audited, so nothing ever fails. It is only visible
        // here, where the intent to audit sits next to the absence of anything to audit.
        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*selected no axis*");
    }

    [Fact]
    public void An_actor_requires_an_identifier()
    {
        var act = () => AuditActor.User("  ");

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Builds a provider with auditing configured through the unified builder.
    /// </summary>
    /// <param name="trail">The recording trail to register.</param>
    /// <param name="resolver">The actor resolver implementation type to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(RecordingAuditTrail trail, Type? resolver = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuditTrail>(trail);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging
                    .UseAuditActorResolver(resolver ?? typeof(TestAuditActorResolver), InstanceLifetime.Scoped)
                    .AddAuditing(auditing => auditing.ForCommands()));

                registry.AddCommands(builder =>
                {
                    builder.Register<CloseAccountCommand>();
                    builder.Register<CloseAccountCommandGuard>();
                    builder.Register<CloseAccountCommandHandler>();
                    builder.Register<ExpireSessionsCommand>();
                    builder.Register<ExpireSessionsCommandHandler>();
                });
            })
            .BuildServiceProvider();
    }
}
