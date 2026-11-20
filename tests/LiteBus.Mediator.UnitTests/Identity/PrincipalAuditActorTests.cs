using System.Security.Claims;
using LiteBus.Auditing;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Auditing.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Identity;

/// <summary>
///     Verifies that an audit record is attributed from the principal on the execution identity when the application
///     registered no actor resolver.
/// </summary>
[Collection("Sequential")]
public sealed class PrincipalAuditActorTests : LiteBusTestBase
{
    [Fact]
    public async Task The_name_identifier_claim_names_the_actor()
    {
        var trail = new IdentityRecordingAuditTrail();
        var provider = BuildProvider(trail);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "user-7"),
                new Claim(ClaimTypes.Name, "Test User"),
            ],
            "test"));

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(
                new AuditedIdentityCommand(),
                new CommandMediationSettings { Identity = ExecutionIdentity.For(principal, "tenant-1") })
            .ConfigureAwait(false);

        var actor = trail.Records.Should().ContainSingle().Which.Actor;
        actor.Should().NotBeNull();
        actor!.Id.Should().Be("user-7");
        actor.Kind.Should().Be(AuditActor.UserKind);
        actor.DisplayName.Should().Be("Test User");
    }

    [Fact]
    public async Task The_subject_claim_names_the_actor_when_no_name_identifier_is_present()
    {
        var trail = new IdentityRecordingAuditTrail();
        var provider = BuildProvider(trail);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "subject-9")],
            "test"));

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(
                new AuditedIdentityCommand(),
                new CommandMediationSettings { Identity = ExecutionIdentity.For(principal, "tenant-1") })
            .ConfigureAwait(false);

        var actor = trail.Records.Should().ContainSingle().Which.Actor;
        actor.Should().NotBeNull();
        actor!.Id.Should().Be("subject-9");
        actor.Kind.Should().Be(AuditActor.UserKind);
        actor.DisplayName.Should().BeNull();
    }

    [Fact]
    public async Task An_identity_without_a_principal_leaves_the_actor_absent()
    {
        var trail = new IdentityRecordingAuditTrail();
        var provider = BuildProvider(trail);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(
                new AuditedIdentityCommand(),
                new CommandMediationSettings { Identity = ExecutionIdentity.ForTenant("tenant-1") })
            .ConfigureAwait(false);

        trail.Records.Should().ContainSingle().Which.Actor.Should().BeNull();
    }

    [Fact]
    public async Task A_mediation_without_an_identity_leaves_the_actor_absent()
    {
        var trail = new IdentityRecordingAuditTrail();
        var provider = BuildProvider(trail);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new AuditedIdentityCommand())
            .ConfigureAwait(false);

        trail.Records.Should().ContainSingle().Which.Actor.Should().BeNull();
    }

    [Fact]
    public async Task A_registered_resolver_wins_over_the_principal()
    {
        var trail = new IdentityRecordingAuditTrail();
        var provider = BuildProvider(trail, useResolver: true);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user-7")],
            "test"));

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(
                new AuditedIdentityCommand(),
                new CommandMediationSettings { Identity = ExecutionIdentity.For(principal, "tenant-1") })
            .ConfigureAwait(false);

        // The resolver states the attribution rule for every message. A principal-derived actor must not silently
        // replace an attribution the application chose to own, even when the identity carries a principal.
        var actor = trail.Records.Should().ContainSingle().Which.Actor;
        actor.Should().NotBeNull();
        actor!.Kind.Should().Be(AuditActor.SystemKind);
        actor.Id.Should().Be("resolver-process");
    }

    [Fact]
    public async Task The_handler_actor_wins_over_the_principal()
    {
        var trail = new IdentityRecordingAuditTrail();
        var provider = BuildProvider(trail);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user-7")],
            "test"));

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(
                new AuditedIdentityCommand { HandlerAttributes = true },
                new CommandMediationSettings { Identity = ExecutionIdentity.For(principal, "tenant-1") })
            .ConfigureAwait(false);

        trail.Records.Should().ContainSingle().Which.Actor!.Id.Should().Be("handler-user");
    }

    /// <summary>
    ///     Builds a provider with auditing enabled on the command axis and no actor resolver unless asked.
    /// </summary>
    /// <param name="trail">The recording trail to register.</param>
    /// <param name="useResolver">Whether to register the fixed actor resolver.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(IdentityRecordingAuditTrail trail, bool useResolver = false)
    {
        var services = new ServiceCollection();

        return services
            .AddLiteBus(registry =>
            {
                registry.AddAuditing(auditing =>
                {
                    auditing.UseTrailInstance(trail);

                    if (useResolver)
                    {
                        auditing.UseActorResolver<FixedAuditActorResolver>();
                    }

                    auditing.ForCommands();
                });

                registry.AddCommands(builder =>
                {
                    builder.Register<AuditedIdentityCommand>();
                    builder.Register<AuditedIdentityCommandHandler>();
                });
            })
            .BuildServiceProvider();
    }
}
