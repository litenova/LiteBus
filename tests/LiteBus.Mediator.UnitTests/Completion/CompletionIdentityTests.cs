using System.Collections.Concurrent;
using System.Security.Claims;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Completion;

/// <summary>
///     Verifies that the completion context carries the mediation identity, and that an audit actor resolver reads it
///     from the context.
/// </summary>
[Collection("Sequential")]
public sealed class CompletionIdentityTests : LiteBusTestBase
{
    [Fact]
    public async Task Completion_carries_the_settings_identity()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildCompletionProvider(recorder, typeof(DirectCompletionHandler));
        var principal = TestPrincipal("user-1");
        var identity = ExecutionIdentity.For(principal, "tenant-completion");

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CompletionCommand(), new CommandMediationSettings { Identity = identity })
            .ConfigureAwait(false);

        var observed = recorder.Observed.Should().ContainSingle().Which.Context;
        observed.Identity.Should().Be(identity);
        observed.Identity.Principal.Should().BeSameAs(principal);
        observed.Identity.TenantId.Should().Be("tenant-completion");
    }

    [Fact]
    public async Task Completion_defaults_to_anonymous_when_no_identity_is_resolved()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildCompletionProvider(recorder, typeof(DirectCompletionHandler));

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CompletionCommand())
            .ConfigureAwait(false);

        recorder.Observed.Should().ContainSingle().Which.Context.Identity.Should().Be(ExecutionIdentity.Anonymous);
    }

    [Fact]
    public async Task Typed_completion_views_expose_the_same_identity()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildCompletionProvider(recorder, typeof(TypedResultCompletionHandler));
        var identity = ExecutionIdentity.ForTenant("tenant-typed");

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CompletionCommandWithResult(), new CommandMediationSettings { Identity = identity })
            .ConfigureAwait(false);

        var observed = recorder.Observed.Should().ContainSingle().Which.Context;
        observed.Identity.Should().Be(identity);
        observed.AsTyped<CompletionCommandWithResult>().Identity.Should().Be(identity);
        observed.AsTyped<CompletionCommandWithResult, string>().Identity.Should().Be(identity);
    }

    [Fact]
    public async Task An_actor_resolver_sees_the_request_identity()
    {
        var trail = new CompletionIdentityAuditTrail();
        var actorIdentities = new ActorIdentityRecorder();
        var provider = BuildAuditProvider(trail, actorIdentities);
        var principal = TestPrincipal("user-7");
        var identity = ExecutionIdentity.For(principal, "tenant-resolver");

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(
                new AuditedCompletionIdentityCommand(),
                new CommandMediationSettings { Identity = identity })
            .ConfigureAwait(false);

        // The resolver reads the caller from the completion context, not from ambient state: what it observed is
        // the identity the mediation ran for.
        actorIdentities.Observed.Should().ContainSingle().Which.Should().Be(identity);

        var actor = trail.Records.Should().ContainSingle().Which.Actor;
        actor.Should().NotBeNull();
        actor!.Id.Should().Be("user-7");
        actor.Kind.Should().Be(AuditActor.UserKind);
    }

    [Fact]
    public void A_manually_built_completion_context_defaults_to_anonymous()
    {
        var context = new MessageCompletionContext
        {
            Message = new object(),
            Outcome = MediationOutcome.Succeeded,
            Duration = TimeSpan.Zero
        };

        context.Identity.Should().Be(ExecutionIdentity.Anonymous);
        context.AsTyped<object>().Identity.Should().Be(ExecutionIdentity.Anonymous);
        context.AsTyped<object, string>().Identity.Should().Be(ExecutionIdentity.Anonymous);
    }

    /// <summary>
    ///     Builds a provider registering the completion commands and the requested completion handlers.
    /// </summary>
    /// <param name="recorder">The recorder shared with the completion handlers.</param>
    /// <param name="completionHandlers">The completion handler types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildCompletionProvider(CompletionRecorder recorder, params Type[] completionHandlers)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<CompletionCommand>();
                    builder.Register<CompletionCommandHandler>();
                    builder.Register<CompletionCommandWithResult>();
                    builder.Register<CompletionCommandWithResultHandler>();

                    foreach (var completionHandler in completionHandlers)
                    {
                        builder.Register(completionHandler);
                    }
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Builds a provider with auditing enabled and an identity-reading actor resolver registered.
    /// </summary>
    /// <param name="trail">The recording trail to register.</param>
    /// <param name="actorIdentities">The recorder the resolver reports observed identities to.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildAuditProvider(
        CompletionIdentityAuditTrail trail,
        ActorIdentityRecorder actorIdentities)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuditTrail>(trail);
        services.AddSingleton(actorIdentities);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging =>
                {
                    messaging.UseAuditActorResolver<IdentityReadingAuditActorResolver>();
                    messaging.AddAuditing(auditing => auditing.ForCommands());
                });

                registry.AddCommands(builder =>
                {
                    builder.Register<AuditedCompletionIdentityCommand>();
                    builder.Register<AuditedCompletionIdentityCommandHandler>();
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Creates a principal carrying a name identifier claim.
    /// </summary>
    /// <param name="id">The identifier the claim carries.</param>
    /// <returns>The principal.</returns>
    private static ClaimsPrincipal TestPrincipal(string id)
    {
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, id)];

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}

/// <summary>
///     A command whose audit position is declared by attribute, so its record can be attributed from the identity.
/// </summary>
[Audited("completion.identity-observe")]
internal sealed class AuditedCompletionIdentityCommand : ICommand;

/// <summary>
///     Handles <see cref="AuditedCompletionIdentityCommand" />.
/// </summary>
internal sealed class AuditedCompletionIdentityCommandHandler : ICommandHandler<AuditedCompletionIdentityCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(AuditedCompletionIdentityCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     Records the identities an audit actor resolver observed.
/// </summary>
internal sealed class ActorIdentityRecorder
{
    /// <summary>
    ///     Gets the identities observed, in the order the resolver ran.
    /// </summary>
    public ConcurrentQueue<ExecutionIdentity> Observed { get; } = new();
}

/// <summary>
///     Attributes an action from the completion identity, the way an application resolver reads the caller.
/// </summary>
internal sealed class IdentityReadingAuditActorResolver : IAuditActorResolver
{
    /// <summary>
    ///     The recorder the observed identities are reported to.
    /// </summary>
    private readonly ActorIdentityRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="IdentityReadingAuditActorResolver" /> class.
    /// </summary>
    /// <param name="recorder">The recorder the observed identities are reported to.</param>
    public IdentityReadingAuditActorResolver(ActorIdentityRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        _recorder = recorder;
    }

    /// <inheritdoc />
    public AuditActor? Resolve(MessageCompletionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _recorder.Observed.Enqueue(context.Identity);

        var id = context.Identity.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrWhiteSpace(id))
        {
            return AuditActor.User(id);
        }

        return context.Identity.HasTenant
            ? AuditActor.For("tenant", context.Identity.TenantId!)
            : null;
    }
}

/// <summary>
///     An in-memory audit trail that records everything written to it.
/// </summary>
internal sealed class CompletionIdentityAuditTrail : IAuditTrail
{
    /// <summary>
    ///     Gets the records written during the test.
    /// </summary>
    public ConcurrentQueue<AuditRecord> Records { get; } = new();

    /// <inheritdoc />
    public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Records.Enqueue(record);
        return Task.CompletedTask;
    }
}
