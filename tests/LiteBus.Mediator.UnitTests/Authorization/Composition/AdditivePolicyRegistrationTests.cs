using System.Security.Claims;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Messaging.Authorization;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Authorization.Composition;

/// <summary>
///     Verifies that repeated <c>AddAuthorizationPolicies</c> calls contribute to one policy set.
/// </summary>
[Collection("Sequential")]
public sealed class AdditivePolicyRegistrationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that policies from repeated calls are all enforced.
    /// </summary>
    [Fact]
    public async Task Policies_from_repeated_calls_are_all_enforced()
    {
        var recorder = new CompositionEvaluationRecorder();
        var provider = BuildProvider(
            recorder,
            new CompositionPermissionOptions(),
            policies => policies.Add(
                "vaults.open",
                new RecordingCompositionPolicy(recorder, "policy:vaults.open", PolicyResult.Allow)),
            policies => policies.Add("vaults.seal", MessagePolicy.Deny("sealing needs a second approver")),
            typeof(OpenCompositionVaultCommand),
            typeof(OpenCompositionVaultCommandHandler),
            typeof(SealCompositionVaultCommand),
            typeof(SealCompositionVaultCommandHandler));
        var mediator = provider.GetRequiredService<ICommandMediator>();
        var open = new OpenCompositionVaultCommand();
        var seal = new SealCompositionVaultCommand();

        await mediator.SendAsync(open).ConfigureAwait(false);

        var act = async () => await mediator.SendAsync(seal).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Reason.Should().Be("sealing needs a second approver");
        open.HandlerRan.Should().BeTrue();
        seal.HandlerRan.Should().BeFalse();
        recorder.Steps.Should().Equal("policy:vaults.open");
    }

    /// <summary>
    ///     Verifies that policies from separate messaging configurations are all enforced.
    /// </summary>
    [Fact]
    public async Task Policies_from_separate_messaging_configurations_are_all_enforced()
    {
        var provider = BuildProviderAcrossConfigurations(
            new CompositionPermissionOptions(),
            [
                messaging => messaging.AddAuthorizationPolicies(policies => policies
                    .Add("vaults.open", MessagePolicy.Allow())),
                messaging => messaging.AddAuthorizationPolicies(policies => policies
                    .Add("vaults.seal", MessagePolicy.Deny("sealing needs a second approver")))
            ],
            typeof(OpenCompositionVaultCommand),
            typeof(OpenCompositionVaultCommandHandler),
            typeof(SealCompositionVaultCommand),
            typeof(SealCompositionVaultCommandHandler));
        var mediator = provider.GetRequiredService<ICommandMediator>();
        var open = new OpenCompositionVaultCommand();
        var seal = new SealCompositionVaultCommand();

        await mediator.SendAsync(open).ConfigureAwait(false);

        var act = async () => await mediator.SendAsync(seal).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Reason.Should().Be("sealing needs a second approver");
        open.HandlerRan.Should().BeTrue();
        seal.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that conditions from repeated calls all run in registration order.
    /// </summary>
    [Fact]
    public async Task Conditions_from_repeated_calls_all_run_in_registration_order()
    {
        var recorder = new CompositionEvaluationRecorder();
        var provider = BuildProvider(
            recorder,
            new CompositionPermissionOptions(),
            policies => policies
                .Add("vaults.open", new RecordingCompositionPolicy(recorder, "policy:vaults.open", PolicyResult.Allow))
                .AddCondition<FirstCompositionCondition>(),
            policies => policies.AddCondition<SecondCompositionCondition>(),
            typeof(OpenCompositionVaultCommand),
            typeof(OpenCompositionVaultCommandHandler));
        var command = new OpenCompositionVaultCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
        recorder.Steps.Should().Equal("first-condition", "second-condition", "policy:vaults.open");
    }

    /// <summary>
    ///     Verifies that a duplicate policy name across calls fails naming the duplicate.
    /// </summary>
    [Fact]
    public void A_duplicate_policy_name_across_calls_fails_naming_the_duplicate()
    {
        var act = () => BuildProvider(
            new CompositionEvaluationRecorder(),
            new CompositionPermissionOptions(),
            policies => policies.Add("vaults.open", MessagePolicy.Allow()),
            policies => policies.Add("vaults.open", MessagePolicy.Deny("closed for stocktake")),
            typeof(OpenCompositionVaultCommand),
            typeof(OpenCompositionVaultCommandHandler));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*vaults.open*");
    }

    /// <summary>
    ///     Verifies that a second permission lookup with another type fails naming both types.
    /// </summary>
    [Fact]
    public void A_second_permission_lookup_with_another_type_fails_naming_both_types()
    {
        var act = () => BuildProvider(
            new CompositionEvaluationRecorder(),
            new CompositionPermissionOptions(),
            policies => policies
                .Add("vaults.open", MessagePolicy.Allow())
                .UsePermissionLookup<GrantingCompositionLookup>(),
            policies => policies.UsePermissionLookup<AlternateCompositionLookup>(),
            typeof(OpenCompositionVaultCommand),
            typeof(OpenCompositionVaultCommandHandler));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*GrantingCompositionLookup*AlternateCompositionLookup*");
    }

    /// <summary>
    ///     Verifies that repeating the same permission lookup still grants through it.
    /// </summary>
    [Fact]
    public async Task Repeating_the_same_permission_lookup_still_grants_through_it()
    {
        var options = new CompositionPermissionOptions();
        options.Granted.Add("vaults.open");
        var provider = BuildProvider(
            new CompositionEvaluationRecorder(),
            options,
            policies => policies
                .Add("vaults.open", MessagePolicy.Permission("vaults.open"))
                .UsePermissionLookup<GrantingCompositionLookup>(),
            policies => policies.UsePermissionLookup<GrantingCompositionLookup>(),
            typeof(OpenCompositionVaultCommand),
            typeof(OpenCompositionVaultCommandHandler));
        var command = new OpenCompositionVaultCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies that a second sign-in instant source with another type fails naming both types.
    /// </summary>
    [Fact]
    public void A_second_sign_in_instant_source_with_another_type_fails_naming_both_types()
    {
        var act = () => BuildProvider(
            new CompositionEvaluationRecorder(),
            new CompositionPermissionOptions(),
            policies => policies
                .Add("vaults.open", MessagePolicy.Allow())
                .UseSignInInstantSource<HandledAtCompositionSource>(),
            policies => policies.UseSignInInstantSource<NullCompositionSource>(),
            typeof(OpenCompositionVaultCommand),
            typeof(OpenCompositionVaultCommandHandler));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*HandledAtCompositionSource*NullCompositionSource*");
    }

    /// <summary>
    ///     Verifies that repeating the same sign-in instant source still reads through it.
    /// </summary>
    [Fact]
    public async Task Repeating_the_same_sign_in_instant_source_still_reads_through_it()
    {
        var provider = BuildProvider(
            new CompositionEvaluationRecorder(),
            new CompositionPermissionOptions(),
            policies => policies
                .Add("vaults.seal", MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(5)))
                .UseSignInInstantSource<HandledAtCompositionSource>(),
            policies => policies.UseSignInInstantSource<HandledAtCompositionSource>(),
            typeof(SealCompositionVaultCommand),
            typeof(SealCompositionVaultCommandHandler));
        var command = new SealCompositionVaultCommand();
        var settings = new CommandMediationSettings
        {
            Identity = ExecutionIdentity.For(SignedInPrincipal("subject-1"))
        };

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command, settings).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies that the registered policy names are exposed on the registry.
    /// </summary>
    [Fact]
    public void Registered_policy_names_are_exposed_on_the_registry()
    {
        var provider = BuildProvider(
            new CompositionEvaluationRecorder(),
            new CompositionPermissionOptions(),
            policies => policies.Add("vaults.open", MessagePolicy.Allow()),
            policies => policies.Add("vaults.seal", MessagePolicy.Allow()),
            typeof(OpenCompositionVaultCommand),
            typeof(OpenCompositionVaultCommandHandler),
            typeof(SealCompositionVaultCommand),
            typeof(SealCompositionVaultCommandHandler));

        provider.GetRequiredService<MessagePolicyRegistry>().PolicyNames
            .Should().BeEquivalentTo("vaults.open", "vaults.seal");
    }

    /// <summary>
    ///     Builds a provider registering the given types with two policy contributions in one configuration.
    /// </summary>
    /// <param name="recorder">Records the evaluations the guard performs.</param>
    /// <param name="options">The permissions the granting lookup grants.</param>
    /// <param name="first">The first policy contribution.</param>
    /// <param name="second">The second policy contribution.</param>
    /// <param name="types">The command and handler types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(
        CompositionEvaluationRecorder recorder,
        CompositionPermissionOptions options,
        Action<MessagePolicyBuilder> first,
        Action<MessagePolicyBuilder> second,
        params Type[] types)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddSingleton(options);

        return services
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

    /// <summary>
    ///     Builds a provider registering the given types with one policy contribution per messaging configuration.
    /// </summary>
    /// <param name="options">The permissions the granting lookup grants.</param>
    /// <param name="configurations">The messaging configurations, each contributing policies.</param>
    /// <param name="types">The command and handler types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProviderAcrossConfigurations(
        CompositionPermissionOptions options,
        Action<MessageModuleBuilder>[] configurations,
        params Type[] types)
    {
        var services = new ServiceCollection();
        services.AddSingleton(options);

        return services
            .AddLiteBus(registry =>
            {
                foreach (var configuration in configurations)
                {
                    registry.AddMessaging(configuration);
                }

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

    /// <summary>
    ///     Creates an authenticated principal carrying no sign-in instant claim.
    /// </summary>
    /// <param name="subjectId">The subject identifier the principal carries.</param>
    /// <returns>The signed-in principal.</returns>
    private static ClaimsPrincipal SignedInPrincipal(string subjectId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subjectId)], "test"));
    }
}
