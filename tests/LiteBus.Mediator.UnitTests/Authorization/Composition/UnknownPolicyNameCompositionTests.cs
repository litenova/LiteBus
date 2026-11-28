using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Messaging.Authorization;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Authorization.Composition;

/// <summary>
///     Verifies that a declared policy name no call registered fails composition before any mediation.
/// </summary>
[Collection("Sequential")]
public sealed class UnknownPolicyNameCompositionTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that unknown policy names fail composition naming every name and each message using it.
    /// </summary>
    [Fact]
    public void Unknown_policy_names_fail_composition_naming_every_name_and_each_message()
    {
        var act = () => BuildProvider(
            policies => policies.Add("vaults.open", MessagePolicy.Allow()),
            typeof(OpenCompositionVaultCommand),
            typeof(OpenCompositionVaultCommandHandler),
            typeof(UnmappedCompositionVaultCommand),
            typeof(UnmappedCompositionVaultCommandHandler),
            typeof(RekeyCompositionVaultCommand),
            typeof(RekeyCompositionVaultCommandHandler),
            typeof(AuditCompositionVaultCommand),
            typeof(AuditCompositionVaultCommandHandler));

        var thrown = act.Should().Throw<LiteBusConfigurationException>().Which;

        thrown.Message.Should().Contain("vaults.unmapped")
            .And.Contain("vaults.audit")
            .And.Contain("UnmappedCompositionVaultCommand")
            .And.Contain("RekeyCompositionVaultCommand")
            .And.Contain("AuditCompositionVaultCommand");
        thrown.Message.Should().NotContain("vaults.open");
        thrown.Message.Should().NotContain("OpenCompositionVaultCommand");
    }

    /// <summary>
    ///     Verifies that declared policy names every call registered compose.
    /// </summary>
    [Fact]
    public void Registered_policy_names_compose()
    {
        var act = () => BuildProvider(
            policies => policies
                .Add("vaults.open", MessagePolicy.Allow())
                .Add("vaults.seal", MessagePolicy.Allow()),
            typeof(OpenCompositionVaultCommand),
            typeof(OpenCompositionVaultCommandHandler),
            typeof(SealCompositionVaultCommand),
            typeof(SealCompositionVaultCommandHandler));

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Builds a provider registering the given types with the policies under test.
    /// </summary>
    /// <param name="configurePolicies">Collects the policies under test.</param>
    /// <param name="types">The command and handler types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(Action<MessagePolicyBuilder> configurePolicies, params Type[] types)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.AddAuthorizationPolicies(configurePolicies));

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
