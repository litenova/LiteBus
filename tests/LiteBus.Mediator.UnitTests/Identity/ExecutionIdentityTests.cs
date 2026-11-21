using System.Security.Claims;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Identity;

/// <summary>
///     Verifies that the mediator resolves one execution identity per mediation in the documented order, and that each
///     semantic mediator carries its settings identity into the request.
/// </summary>
[Collection("Sequential")]
public sealed class ExecutionIdentityTests : LiteBusTestBase
{
    [Fact]
    public async Task A_settings_identity_reaches_the_command_handler()
    {
        var recorder = new IdentityRecorder();
        var provider = BuildProvider(recorder);
        var principal = TestPrincipal("user-1");
        var identity = ExecutionIdentity.For(principal, "tenant-settings");

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new InnerIdentityCommand(), new CommandMediationSettings { Identity = identity })
            .ConfigureAwait(false);

        var observed = recorder.ForStage("inner").Should().ContainSingle().Subject;
        observed.TenantId.Should().Be("tenant-settings");
        observed.Principal.Should().BeSameAs(principal);
    }

    [Fact]
    public async Task A_settings_identity_wins_over_the_registered_provider()
    {
        var recorder = new IdentityRecorder();
        var identityProvider = new StubExecutionIdentityProvider(ExecutionIdentity.ForTenant("tenant-provider"));
        var provider = BuildProvider(recorder, identityProvider);
        var principal = TestPrincipal("user-1");
        var identity = ExecutionIdentity.For(principal, "tenant-settings");

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new InnerIdentityCommand(), new CommandMediationSettings { Identity = identity })
            .ConfigureAwait(false);

        var observed = recorder.ForStage("inner").Should().ContainSingle().Subject;
        observed.TenantId.Should().Be("tenant-settings");
        observed.Principal.Should().BeSameAs(principal);
        identityProvider.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task A_nested_mediation_inherits_the_enclosing_identity()
    {
        var recorder = new IdentityRecorder();
        var identityProvider = new StubExecutionIdentityProvider(ExecutionIdentity.ForTenant("tenant-provider"));
        var provider = BuildProvider(recorder, identityProvider);
        var principal = TestPrincipal("user-1");
        var identity = ExecutionIdentity.For(principal, "tenant-outer");

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new OuterIdentityCommand(), new CommandMediationSettings { Identity = identity })
            .ConfigureAwait(false);

        recorder.ForStage("outer").Should().ContainSingle().Which.Should().Be(identity);
        recorder.ForStage("inner").Should().ContainSingle().Which.Should().Be(identity);
        identityProvider.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task An_explicit_inner_identity_wins_over_the_enclosing_identity()
    {
        var recorder = new IdentityRecorder();
        var provider = BuildProvider(recorder);
        var outer = ExecutionIdentity.ForTenant("tenant-outer");
        var inner = ExecutionIdentity.ForTenant("tenant-inner");

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(
                new OuterIdentityCommand { InnerIdentity = inner },
                new CommandMediationSettings { Identity = outer })
            .ConfigureAwait(false);

        recorder.ForStage("outer").Should().ContainSingle().Which.Should().Be(outer);
        recorder.ForStage("inner").Should().ContainSingle().Which.Should().Be(inner);
    }

    [Fact]
    public async Task The_registered_provider_supplies_the_identity_when_no_settings_do()
    {
        var recorder = new IdentityRecorder();
        var identityProvider = new StubExecutionIdentityProvider(ExecutionIdentity.ForTenant("tenant-provider"));
        var provider = BuildProvider(recorder, identityProvider);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new InnerIdentityCommand())
            .ConfigureAwait(false);

        recorder.ForStage("inner").Should().ContainSingle().Which.TenantId.Should().Be("tenant-provider");
        identityProvider.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task A_mediation_with_no_identity_is_anonymous()
    {
        var recorder = new IdentityRecorder();
        var provider = BuildProvider(recorder);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new InnerIdentityCommand())
            .ConfigureAwait(false);

        recorder.ForStage("inner").Should().ContainSingle().Which.Should().Be(ExecutionIdentity.Anonymous);
    }

    [Fact]
    public async Task A_settings_identity_reaches_the_query_handler()
    {
        var recorder = new IdentityRecorder();
        var provider = BuildProvider(recorder);
        var identity = ExecutionIdentity.ForTenant("tenant-settings");

        var result = await provider.GetRequiredService<IQueryMediator>()
            .QueryAsync(new RecordIdentityQuery(), new QueryMediationSettings { Identity = identity })
            .ConfigureAwait(false);

        result.Should().Be("ok");
        recorder.ForStage("query").Should().ContainSingle().Which.Should().Be(identity);
    }

    [Fact]
    public async Task A_settings_identity_reaches_the_event_handler()
    {
        var recorder = new IdentityRecorder();
        var provider = BuildProvider(recorder);
        var identity = ExecutionIdentity.ForTenant("tenant-settings");

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new RecordIdentityEvent(), new EventMediationSettings { Identity = identity })
            .ConfigureAwait(false);

        recorder.ForStage("event").Should().ContainSingle().Which.Should().Be(identity);
    }

    [Fact]
    public async Task A_try_send_carries_the_settings_identity()
    {
        var recorder = new IdentityRecorder();
        var provider = BuildProvider(recorder);
        var identity = ExecutionIdentity.ForTenant("tenant-settings");

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new InnerIdentityCommand(), new CommandMediationSettings { Identity = identity })
            .ConfigureAwait(false);

        result.Outcome.Should().Be(MediationOutcome.Succeeded);
        recorder.ForStage("inner").Should().ContainSingle().Which.Should().Be(identity);
    }

    [Fact]
    public async Task A_guard_reads_the_current_identity()
    {
        var recorder = new IdentityRecorder();
        var provider = BuildProvider(recorder);
        var principal = TestPrincipal("user-1");
        var identity = ExecutionIdentity.For(principal, "tenant-settings");

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new AuthorizedIdentityCommand(), new CommandMediationSettings { Identity = identity })
            .ConfigureAwait(false);

        var observed = recorder.ForStage("guard").Should().ContainSingle().Subject;
        observed.TenantId.Should().Be("tenant-settings");
        observed.Principal.Should().BeSameAs(principal);
    }

    /// <summary>
    ///     Builds a provider registering the identity commands, query, event, guard, and their handlers.
    /// </summary>
    /// <param name="recorder">The recorder shared with the handlers and the guard.</param>
    /// <param name="identityProvider">The identity provider to register, or <see langword="null" /> for none.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(IdentityRecorder recorder, IExecutionIdentityProvider? identityProvider = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        if (identityProvider is not null)
        {
            services.AddSingleton(identityProvider);
        }

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<OuterIdentityCommand>();
                    builder.Register<OuterIdentityCommandHandler>();
                    builder.Register<InnerIdentityCommand>();
                    builder.Register<InnerIdentityCommandHandler>();
                    builder.Register<AuthorizedIdentityCommand>();
                    builder.Register<AuthorizedIdentityCommandHandler>();
                    builder.Register<IdentityObservingAuthorizationGuard<AuthorizedIdentityCommand>>();
                });

                registry.AddQueries(builder =>
                {
                    builder.Register<RecordIdentityQuery>();
                    builder.Register<RecordIdentityQueryHandler>();
                });

                registry.AddEvents(builder =>
                {
                    builder.Register<RecordIdentityEvent>();
                    builder.Register<RecordIdentityEventHandler>();
                });
            })
            .BuildServiceProvider();
    }

    private static ClaimsPrincipal TestPrincipal(string id)
    {
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, id)];

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
