using LiteBus.Commands;
using LiteBus.Messaging;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Messaging.Authorization;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Authorization.Composition;

/// <summary>
///     Verifies that an identical policy repeat is a no-op while a conflicting one fails naming both policies and pointing at Replace.
/// </summary>
[Collection("Sequential")]
public sealed class PolicyRegistrationConflictTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that repeating the same policy across calls composes and enforces it.
    /// </summary>
    [Fact]
    public async Task An_identical_policy_repeat_across_calls_composes_and_enforces_it()
    {
        var provider = BuildProvider(
            policies => policies.Add("vaults.open", MessagePolicy.Allow()),
            policies => policies.Add("vaults.open", MessagePolicy.Allow()),
            typeof(OpenCompositionVaultCommand),
            typeof(OpenCompositionVaultCommandHandler));
        var command = new OpenCompositionVaultCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
        provider.GetRequiredService<MessagePolicyRegistry>().Policies.Should().ContainSingle()
            .Which.Value.Should().Be(MessagePolicy.Allow());
    }

    /// <summary>
    ///     Verifies that a conflicting policy repeat fails naming the name, both policies, and Replace.
    /// </summary>
    [Fact]
    public void A_conflicting_policy_repeat_fails_naming_the_name_both_policies_and_replace()
    {
        var act = () => new MessagePolicyBuilder()
            .Add("vaults.open", MessagePolicy.Allow())
            .Add("vaults.open", MessagePolicy.Deny("closed for stocktake"));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*vaults.open*AllowPolicy*DenyPolicy*Replace*");
    }

    /// <summary>
    ///     Verifies that a conflicting policy repeat across calls fails pointing at Replace.
    /// </summary>
    [Fact]
    public void A_conflicting_policy_repeat_across_calls_fails_pointing_at_replace()
    {
        var act = () => BuildProvider(
            policies => policies.Add("vaults.open", MessagePolicy.Allow()),
            policies => policies.Add("vaults.open", MessagePolicy.Deny("closed for stocktake")),
            typeof(OpenCompositionVaultCommand),
            typeof(OpenCompositionVaultCommandHandler));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*vaults.open*Replace*");
    }

    /// <summary>
    ///     Verifies that Replace overwrites the registered policy.
    /// </summary>
    [Fact]
    public void Replace_overwrites_the_registered_policy()
    {
        var builder = new MessagePolicyBuilder()
            .Add("vaults.open", MessagePolicy.Allow())
            .Replace("vaults.open", MessagePolicy.Deny("closed for stocktake"));

        builder.Policies["vaults.open"].Should().Be(MessagePolicy.Deny("closed for stocktake"));
    }

    /// <summary>
    ///     Verifies that Replace registers the policy when none is recorded under the name.
    /// </summary>
    [Fact]
    public void Replace_registers_the_policy_when_none_is_recorded()
    {
        var builder = new MessagePolicyBuilder()
            .Replace("vaults.open", MessagePolicy.Allow());

        builder.Policies["vaults.open"].Should().Be(MessagePolicy.Allow());
    }

    /// <summary>
    ///     Verifies that a replaced policy is the one the guard enforces.
    /// </summary>
    [Fact]
    public async Task A_replaced_policy_is_the_one_the_guard_enforces()
    {
        var provider = BuildProvider(
            policies => policies.Add("vaults.open", MessagePolicy.Allow()),
            policies => policies.Replace("vaults.open", MessagePolicy.Deny("closed for stocktake")),
            typeof(OpenCompositionVaultCommand),
            typeof(OpenCompositionVaultCommandHandler));
        var command = new OpenCompositionVaultCommand();

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Reason.Should().Be("closed for stocktake");
        command.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that Replace rejects a blank name where it is written.
    /// </summary>
    /// <param name="name">The blank name under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Replace_rejects_a_blank_name_where_it_is_written(string? name)
    {
        var act = () => new MessagePolicyBuilder().Replace(name!, MessagePolicy.Allow());

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Verifies that Replace rejects a null policy.
    /// </summary>
    [Fact]
    public void Replace_rejects_a_null_policy()
    {
        var act = () => new MessagePolicyBuilder().Replace("vaults.open", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    ///     Builds a provider registering the given types with two policy contributions in one configuration.
    /// </summary>
    /// <param name="first">The first policy contribution.</param>
    /// <param name="second">The second policy contribution.</param>
    /// <param name="types">The command and handler types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(
        Action<MessagePolicyBuilder> first,
        Action<MessagePolicyBuilder> second,
        params Type[] types)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging
                    .AddAuthorizationPolicies(first)
                    .AddAuthorizationPolicies(second));

                registry.AddCommands(builder =>
                {
                    foreach (var type in types)
                    {
                        builder.Register(type);
                    }
                });
            })
            .BuildServiceProvider();
    }
}
