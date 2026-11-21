using System;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Messaging.Authorization;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Authorization.Policies;

/// <summary>
///     Verifies that the policy builder rejects blank names, missing policies, and duplicate registrations.
/// </summary>
[Collection("Sequential")]
public sealed class MessagePolicyBuilderTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that a blank policy name is rejected where it is written.
    /// </summary>
    /// <param name="name">The blank name under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_policy_name_is_rejected_where_it_is_written(string? name)
    {
        var act = () => new MessagePolicyBuilder().Add(name!, MessagePolicy.Allow());

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Verifies that a null policy is rejected.
    /// </summary>
    [Fact]
    public void A_null_policy_is_rejected()
    {
        var act = () => new MessagePolicyBuilder().Add("vaults.open", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    ///     Verifies that a conflicting policy name fails naming the duplicate.
    /// </summary>
    [Fact]
    public void A_duplicate_policy_name_fails_naming_the_duplicate()
    {
        var act = () => new MessagePolicyBuilder()
            .Add("vaults.open", MessagePolicy.Allow())
            .Add("vaults.open", MessagePolicy.Deny("closed for stocktake"));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*vaults.open*");
    }

    /// <summary>
    ///     Verifies that an identical policy repeat is a no-op.
    /// </summary>
    [Fact]
    public void An_identical_policy_repeat_is_a_noop()
    {
        var builder = new MessagePolicyBuilder()
            .Add("vaults.open", MessagePolicy.Allow())
            .Add("vaults.open", MessagePolicy.Allow());

        builder.Policies.Should().ContainSingle()
            .Which.Value.Should().Be(MessagePolicy.Allow());
    }

    /// <summary>
    ///     Verifies that a null builder is rejected.
    /// </summary>
    [Fact]
    public void A_null_builder_is_rejected()
    {
        var act = () => ((MessageModuleBuilder) null!).AddAuthorizationPolicies(_ => { });

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    ///     Verifies that a null configuration callback fails composition.
    /// </summary>
    [Fact]
    public void A_null_configuration_callback_fails_composition()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(registry => registry.AddMessaging(messaging =>
                messaging.AddAuthorizationPolicies(null!)));

        act.Should().Throw<ArgumentNullException>();
    }
}
