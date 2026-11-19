using System.Reflection;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Messaging.Abstractions.Tenancy;
using LiteBus.Messaging.Authorization;
using LiteBus.Messaging.Tenancy;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Tenancy;

/// <summary>
///     Verifies that the authorization guard runs before the tenancy guard whatever the registration order.
/// </summary>
[Collection("Sequential")]
public sealed class GuardOrderTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that a signed-out caller sees the authorization refusal before the tenancy refusal.
    /// </summary>
    [Fact]
    public async Task A_signed_out_caller_sees_the_authorization_refusal_before_the_tenancy_refusal()
    {
        var provider = BuildProvider(messaging => messaging
            .UseTenancy()
            .AddAuthorizationPolicies(policies => policies.Add("vaults.post", MessagePolicy.SignedIn())));
        var command = new PostGuardedEntryCommand();

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(command).ConfigureAwait(false);

        result.Outcome.Should().Be(MediationOutcome.Denied);
        result.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSignedIn);
        command.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that guard order does not depend on registration order.
    /// </summary>
    [Fact]
    public async Task Guard_order_does_not_depend_on_registration_order()
    {
        var provider = BuildProvider(messaging => messaging
            .AddAuthorizationPolicies(policies => policies.Add("vaults.post", MessagePolicy.SignedIn()))
            .UseTenancy());
        var command = new PostGuardedEntryCommand();

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(command).ConfigureAwait(false);

        result.Outcome.Should().Be(MediationOutcome.Denied);
        result.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSignedIn);
        command.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that the guards carry the stable authorization and tenancy priorities.
    /// </summary>
    [Fact]
    public void The_guards_carry_the_stable_authorization_and_tenancy_priorities()
    {
        var authorization = typeof(PolicyAuthorizationGuard<PostGuardedEntryCommand>)
            .GetCustomAttribute<HandlerPriorityAttribute>();
        var tenancy = typeof(TenancyGuard<PostGuardedEntryCommand>)
            .GetCustomAttribute<HandlerPriorityAttribute>();

        authorization.Should().NotBeNull();
        authorization!.Priority.Should().Be(HandlerPriorities.Authorization);
        tenancy.Should().NotBeNull();
        tenancy!.Priority.Should().Be(HandlerPriorities.Tenancy);
        HandlerPriorities.Authorization.Should().Be(HandlerPriorities.ReservedFloor + 10);
        HandlerPriorities.Tenancy.Should().Be(HandlerPriorities.ReservedFloor + 20);
    }

    /// <summary>
    ///     Builds a provider registering the guarded command with the messaging configuration under test.
    /// </summary>
    /// <param name="configureMessaging">Configures tenancy and authorization policies under test.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(Action<MessageModuleBuilder> configureMessaging)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(configureMessaging);

                registry.AddCommands(builder =>
                {
                    builder.Register<PostGuardedEntryCommand>();
                    builder.Register<PostGuardedEntryCommandHandler>();
                });
            })
            .BuildServiceProvider();
    }
}

/// <summary>
///     A command that requires both a tenant and the vaults.post policy.
/// </summary>
[TenantRequired]
[Authorized("vaults.post")]
internal sealed class PostGuardedEntryCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="PostGuardedEntryCommand" />.
/// </summary>
internal sealed class PostGuardedEntryCommandHandler : ICommandHandler<PostGuardedEntryCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(PostGuardedEntryCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}
