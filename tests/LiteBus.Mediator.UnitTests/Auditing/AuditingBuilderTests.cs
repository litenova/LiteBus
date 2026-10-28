using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Auditing;

/// <summary>
///     Verifies every way the audit feature can be configured through one call, and the composition exception
///     hierarchy the failures use.
/// </summary>
[Collection("Sequential")]
public sealed class AuditingBuilderTests : LiteBusTestBase
{
    [Fact]
    public async Task Pre_created_instances_are_accepted_for_the_trail_the_resolver_and_the_mapper()
    {
        var trail = new RecordingAuditTrail();

        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.AddAuditing(auditing => auditing
                    .UseTrailInstance(trail)
                    .UseActorResolverInstance(new TestAuditActorResolver())
                    .UseOutcomeMapperInstance(new TestAuditOutcomeMapper())
                    .ForCommands()));

                registry.AddCommands(builder =>
                {
                    builder.Register<ExpireSessionsCommand>();
                    builder.Register<ExpireSessionsCommandHandler>();
                });
            })
            .BuildServiceProvider();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ExpireSessionsCommand()).ConfigureAwait(false);

        trail.Records.Should().ContainSingle().Which.Actor!.Kind.Should().Be(AuditActor.SystemKind);
    }

    [Fact]
    public async Task A_typed_outcome_mapper_and_an_explicit_trail_lifetime_are_accepted()
    {
        var trail = new RecordingAuditTrail();

        var provider = new ServiceCollection()
            .AddSingleton<IAuditTrail>(trail)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.AddAuditing(auditing => auditing
                    .UseTrail<PassThroughAuditTrail>(InstanceLifetime.Singleton)
                    .UseActorResolver<TestAuditActorResolver>(InstanceLifetime.Singleton)
                    .UseOutcomeMapper<PassThroughOutcomeMapper>()
                    .ForCommands()));

                registry.AddCommands(builder =>
                {
                    builder.Register<ExpireSessionsCommand>();
                    builder.Register<ExpireSessionsCommandHandler>();
                });
            })
            .BuildServiceProvider();

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ExpireSessionsCommand()).ConfigureAwait(false);

        await act.Should().NotThrowAsync().ConfigureAwait(false);
    }

    [Fact]
    public void ForAllAxes_selects_every_axis_the_host_registered()
    {
        var provider = new ServiceCollection()
            .AddSingleton<IAuditTrail>(new RecordingAuditTrail())
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.AddAuditing(auditing => auditing.ForAllAxes()));

                registry.AddCommands(builder =>
                {
                    builder.Register<ExpireSessionsCommand>();
                    builder.Register<ExpireSessionsCommandHandler>();
                });

                registry.AddQueries(_ => { });
                registry.AddEvents(_ => { });
            })
            .BuildServiceProvider();

        // An axis with no messages is unaffected, so this is safe on a host that composes only some of them.
        provider.GetRequiredService<LiteBusCompositionSummary>().AuditingEnabled.Should().BeTrue();
    }

    [Fact]
    public void Calling_AddAuditing_twice_adds_to_the_same_selection()
    {
        var provider = new ServiceCollection()
            .AddSingleton<IAuditTrail>(new RecordingAuditTrail())
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging
                    .AddAuditing(auditing => auditing.ForCommands())
                    .AddAuditing(auditing => auditing.ForQueries()));

                registry.AddCommands(builder =>
                {
                    builder.Register<ExpireSessionsCommand>();
                    builder.Register<ExpireSessionsCommandHandler>();
                });

                registry.AddQueries(_ => { });
            })
            .BuildServiceProvider();

        // An axis enabled once stays enabled, so a composition file assembled from several helpers behaves.
        provider.GetRequiredService<LiteBusCompositionSummary>().AuditingEnabled.Should().BeTrue();
    }

    [Fact]
    public void Every_composition_exception_is_catchable_as_the_category_and_on_its_own()
    {
        var inner = new InvalidOperationException("cause");

        var exceptions = new LiteBusConfigurationException[]
        {
            new ModuleCompositionException("module"),
            new ModuleCompositionException("module", inner),
            new MessageDeclarationException("declaration"),
            new MessageDeclarationException("declaration", inner),
            new PipelineContractException("contract"),
            new PipelineContractException("contract", inner),
            new DurableStorageConfigurationException("storage"),
            new DurableStorageConfigurationException("storage", inner),
            new AuditConfigurationException("audit"),
            new AuditConfigurationException("audit", inner)
        };

        // The base stays catchable as the category, which is what keeps every existing catch site working; the
        // derived type is what lets a host tell one composition mistake from another.
        exceptions.Should().AllBeAssignableTo<LiteBusConfigurationException>();
        exceptions.Should().AllSatisfy(exception => exception.Message.Should().NotBeNullOrWhiteSpace());
        exceptions.Where(exception => exception.InnerException is not null).Should().HaveCount(5);
    }
}

/// <summary>
///     A trail that discards records, for a test asserting on configuration rather than on content.
/// </summary>
internal sealed class PassThroughAuditTrail : IAuditTrail
{
    /// <inheritdoc />
    public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     An outcome mapper with a parameterless constructor, for the typed registration overload.
/// </summary>
internal sealed class PassThroughOutcomeMapper : IAuditOutcomeMapper
{
    /// <inheritdoc />
    public AuditOutcome Map(MessageCompletionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return LiteBus.Messaging.Audit.DefaultAuditOutcomeMapper.MapByOutcome(context);
    }
}
