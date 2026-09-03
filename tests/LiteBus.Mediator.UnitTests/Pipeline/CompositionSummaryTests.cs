using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Mediator.UnitTests.Completion;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Queries;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Verifies that the host reports what it composed, including every open generic handler and how many messages it
///     was closed over.
/// </summary>
/// <remarks>
///     The open generic line is the point. Adding one file to a scanned assembly inserts a pipeline stage into every
///     message it fits, and nothing in the composition code shows it. A count that changes when the set changes is
///     what makes it reviewable.
/// </remarks>
[Collection("Sequential")]
public sealed class CompositionSummaryTests : LiteBusTestBase
{
    [Fact]
    public void The_summary_counts_messages_per_axis()
    {
        var summary = Build().GetRequiredService<LiteBusCompositionSummary>();

        summary.MessageCountsByAxis.Should().ContainKey("commands");
        summary.MessageCountsByAxis["commands"].Should().BeGreaterThan(0);
        summary.MessageCountsByAxis.Should().ContainKey("queries");
        summary.MessageCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void An_axis_the_host_does_not_compose_is_absent_rather_than_zero()
    {
        var summary = Build().GetRequiredService<LiteBusCompositionSummary>();

        // Saying what was wired is more useful than reporting zero for something nobody asked for.
        summary.MessageCountsByAxis.Should().NotContainKey("events");
    }

    [Fact]
    public void The_summary_names_each_open_generic_and_the_messages_it_reached()
    {
        var summary = Build().GetRequiredService<LiteBusCompositionSummary>();

        var closure = summary.OpenGenericHandlers
            .Should().ContainSingle(handler => handler.HandlerName.StartsWith("SharedCommandGuard", StringComparison.Ordinal))
            .Subject;

        closure.MessageCount.Should().Be(1);
    }

    [Fact]
    public void The_summary_reports_the_audit_trail_and_its_lifetime()
    {
        var provider = new ServiceCollection()
            .AddSingleton(new CompletionRecorder())
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.AddAuditing(auditing => auditing
                    .UseTrail<NullAuditTrail>()
                    .ForCommands()));

                registry.AddCommands(builder =>
                {
                    builder.Register<CompletionCommand>();
                    builder.Register<CompletionCommandHandler>();
                });
            })
            .BuildServiceProvider();

        var summary = provider.GetRequiredService<LiteBusCompositionSummary>();

        summary.AuditingEnabled.Should().BeTrue();
        summary.AuditTrail.Should().Be("NullAuditTrail (Scoped)");
        summary.AuditActorResolverRegistered.Should().BeFalse();
        summary.ToString().Should().Contain("actor resolver missing");
    }

    [Fact]
    public void The_summary_reports_each_declaration_policy_with_its_scope()
    {
        // A predicate that selects nothing, so the summary can be read without the requirement also failing.
        var provider = Build(messaging =>
            messaging.RequireDeclaration<AuditDeclaration>(static _ => false, "every archived command"));

        var summary = provider.GetRequiredService<LiteBusCompositionSummary>();

        summary.RequiredDeclarations.Should().ContainSingle()
            .Which.Should().Be("AuditDeclaration of every archived command");
        summary.ToString().Should().Contain("required declarations AuditDeclaration of every archived command");
    }

    [Fact]
    public void An_open_generic_that_fits_nothing_is_reported_as_covering_no_messages()
    {
        var provider = new ServiceCollection()
            .AddSingleton(new CrossAxisRecorder())
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                // No command is registered, so the guard closes over nothing and never runs.
                registry.AddCommands(builder => builder.Register(typeof(SharedCommandGuard<>)));
            })
            .BuildServiceProvider();

        provider.GetRequiredService<LiteBusCompositionSummary>().OpenGenericHandlers
            .Should().ContainSingle()
            .Which.MessageCount.Should().Be(0);
    }

    [Fact]
    public void A_scanned_open_generic_is_recorded_as_scanned_and_a_named_one_is_not()
    {
        var scanned = new MessageRegistry();
        scanned.Register(typeof(ApproveLeaveCommand));
        scanned.RegisterFromScan(typeof(SharedCommandGuard<>));

        scanned.ScannedOpenGenericHandlers.Should().ContainSingle()
            .Which.Should().Be(typeof(SharedCommandGuard<>));

        var named = new MessageRegistry();
        named.Register(typeof(ApproveLeaveCommand));
        named.Register(typeof(SharedCommandGuard<>));

        // Both close over the command; only the origin differs, which is the whole distinction strict mode needs.
        named.ScannedOpenGenericHandlers.Should().BeEmpty();
        named.OpenGenericClosures[typeof(SharedCommandGuard<>)].Should().ContainSingle();
    }

    [Fact]
    public void Strict_mode_reports_an_open_generic_that_arrived_through_a_scan()
    {
        var registry = new MessageRegistry();
        registry.Register(typeof(ApproveLeaveCommand));
        registry.RegisterFromScan(typeof(SharedCommandGuard<>));

        var act = () => MessageModule.ThrowIfOpenGenericsWereScanned(registry);

        act.Should().Throw<PipelineContractException>()
            .WithMessage("*discovered by assembly scanning*SharedCommandGuard*Register(typeof(SharedCommandGuard<>))*");
    }

    [Fact]
    public void Strict_mode_accepts_an_open_generic_the_composition_code_names()
    {
        var act = () => new ServiceCollection()
            .AddSingleton(new CrossAxisRecorder())
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.RequireExplicitOpenGenerics());

                registry.AddCommands(builder =>
                {
                    builder.Register<ApproveLeaveCommand>();
                    builder.Register<ApproveLeaveCommandHandler>();
                    builder.Register(typeof(SharedCommandGuard<>));
                });
            });

        act.Should().NotThrow();
    }

    [Fact]
    public void Scanning_still_closes_open_generics_by_default()
    {
        var registry = new MessageRegistry();
        registry.Register(typeof(ApproveLeaveCommand));
        registry.RegisterFromScan(typeof(SharedCommandGuard<>));

        // Picking up open generic handlers is what a scan has meant since v4, so strict mode is opt-in and a scan
        // still closes them over everything they fit.
        registry.OpenGenericClosures[typeof(SharedCommandGuard<>)]
            .Should().ContainSingle()
            .Which.Should().Be(typeof(ApproveLeaveCommand));
    }

    /// <summary>
    ///     Builds a provider over one command, one query, and one open generic guard.
    /// </summary>
    /// <param name="messaging">The extra messaging configuration.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider Build(Action<MessageModuleBuilder>? messaging = null)
    {
        return new ServiceCollection()
            .AddSingleton(new CrossAxisRecorder())
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging ?? (_ => { }));

                registry.AddCommands(builder =>
                {
                    builder.Register<ApproveLeaveCommand>();
                    builder.Register<ApproveLeaveCommandHandler>();
                    builder.Register(typeof(SharedCommandGuard<>));
                });

                registry.AddQueries(builder =>
                {
                    builder.Register<ListLeaveQuery>();
                    builder.Register<ListLeaveQueryHandler>();
                });
            })
            .BuildServiceProvider();
    }
}
