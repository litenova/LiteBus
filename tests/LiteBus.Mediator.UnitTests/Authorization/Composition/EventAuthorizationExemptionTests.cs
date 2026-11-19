using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Messaging.Authorization;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Authorization.Composition;

/// <summary>
///     Verifies that events publish without an authorization declaration while an explicit per-event position still applies.
/// </summary>
[Collection("Sequential")]
public sealed class EventAuthorizationExemptionTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that an undeclared event publishes with authorization policies registered.
    /// </summary>
    [Fact]
    public async Task An_undeclared_event_publishes_with_authorization_policies_registered()
    {
        var provider = BuildProvider(
            policies => policies.Add("vaults.open", MessagePolicy.Deny("closed for stocktake")),
            typeof(VaultOpenedEvent),
            typeof(VaultOpenedEventHandler));
        var @event = new VaultOpenedEvent();

        await provider.GetRequiredService<IEventMediator>().PublishAsync(@event).ConfigureAwait(false);

        @event.HandlerRan.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies that an undeclared event declares the module exempt position.
    /// </summary>
    [Fact]
    public void An_undeclared_event_declares_the_module_exempt_position()
    {
        var accessor = BuildProvider(
                policies => policies.Add("vaults.open", MessagePolicy.Allow()),
                typeof(VaultOpenedEvent),
                typeof(VaultOpenedEventHandler))
            .GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<VaultOpenedEvent, AuthorizationDeclaration>(out var declaration).Should().BeTrue();
        declaration.Should().BeOfType<AuthorizationExemptDeclaration>()
            .Which.Rationale.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    ///     Verifies that an explicit authorized declaration on an event still applies.
    /// </summary>
    [Fact]
    public async Task An_explicit_authorized_declaration_on_an_event_still_applies()
    {
        var provider = BuildProvider(
            policies => policies.Add("vaults.open", MessagePolicy.SignedIn()),
            typeof(RestrictedVaultOpenedEvent),
            typeof(RestrictedVaultOpenedEventHandler));
        var @event = new RestrictedVaultOpenedEvent();

        var act = async () => await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(@event).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSignedIn);
        @event.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that an explicit exempt declaration on an event publishes beside the module default.
    /// </summary>
    [Fact]
    public async Task An_explicit_exempt_declaration_on_an_event_publishes_beside_the_module_default()
    {
        var provider = BuildProvider(
            policies => policies.Add("vaults.open", MessagePolicy.Deny("closed for stocktake")),
            typeof(PublicVaultOpenedEvent),
            typeof(PublicVaultOpenedEventHandler));
        var @event = new PublicVaultOpenedEvent();

        await provider.GetRequiredService<IEventMediator>().PublishAsync(@event).ConfigureAwait(false);

        @event.HandlerRan.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies that a composition-declared event default is kept over the module default.
    /// </summary>
    [Fact]
    public async Task A_composition_declared_event_default_is_kept_over_the_module_default()
    {
        var provider = BuildProviderWithEventDefault(
            AuthorizationDeclaration.Authorized("vaults.open"),
            policies => policies.Add("vaults.open", MessagePolicy.SignedIn()),
            typeof(VaultOpenedEvent),
            typeof(VaultOpenedEventHandler));
        var @event = new VaultOpenedEvent();

        var act = async () => await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(@event).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSignedIn);
        @event.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Builds a provider registering the given types with the policies under test.
    /// </summary>
    /// <param name="configurePolicies">Collects the policies under test.</param>
    /// <param name="types">The event and handler types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(Action<MessagePolicyBuilder> configurePolicies, params Type[] types)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.AddAuthorizationPolicies(configurePolicies));

                registry.AddEvents(builder =>
                {
                    foreach (var type in types)
                    {
                        builder.Register(type);
                    }
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Builds a provider that also declares its own authorization default for every event.
    /// </summary>
    /// <param name="eventDefault">The authorization position the composition declares for every event.</param>
    /// <param name="configurePolicies">Collects the policies under test.</param>
    /// <param name="types">The event and handler types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProviderWithEventDefault(
        AuthorizationDeclaration eventDefault,
        Action<MessagePolicyBuilder> configurePolicies,
        params Type[] types)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging
                    .DeclareDefault<IEvent, AuthorizationDeclaration>(eventDefault)
                    .AddAuthorizationPolicies(configurePolicies));

                registry.AddEvents(builder =>
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
