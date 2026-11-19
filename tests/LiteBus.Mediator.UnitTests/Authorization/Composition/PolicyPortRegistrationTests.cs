using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Messaging.Authorization;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Authorization.Composition;

/// <summary>
///     Verifies that policy-scoped conditions and the sign-in instant source resolve from the dispatch scope.
/// </summary>
[Collection("Sequential")]
public sealed class PolicyPortRegistrationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that a policy-scoped condition runs only where the policy names it.
    /// </summary>
    [Fact]
    public async Task A_policy_scoped_condition_runs_only_where_the_policy_names_it()
    {
        var recorder = new CompositionEvaluationRecorder();
        var provider = BuildProvider(
            recorder,
            policies => policies
                .Add("vaults.open", MessagePolicy.Condition<NamedCompositionCondition>())
                .Add("vaults.seal", MessagePolicy.Allow())
                .RegisterCondition<NamedCompositionCondition>(),
            typeof(OpenCompositionVaultCommand),
            typeof(OpenCompositionVaultCommandHandler),
            typeof(SealCompositionVaultCommand),
            typeof(SealCompositionVaultCommandHandler));
        var mediator = provider.GetRequiredService<ICommandMediator>();
        var open = new OpenCompositionVaultCommand();
        var seal = new SealCompositionVaultCommand();

        await mediator.SendAsync(open).ConfigureAwait(false);

        recorder.Steps.Should().Equal("named-condition");

        await mediator.SendAsync(seal).ConfigureAwait(false);

        open.HandlerRan.Should().BeTrue();
        seal.HandlerRan.Should().BeTrue();
        recorder.Steps.Should().Equal("named-condition");
    }

    /// <summary>
    ///     Verifies that a policy-scoped condition missing from the scope denies with the stable code.
    /// </summary>
    [Fact]
    public async Task A_policy_scoped_condition_missing_from_the_scope_denies_with_the_stable_code()
    {
        var provider = BuildProvider(
            new CompositionEvaluationRecorder(),
            policies => policies.Add("vaults.open", MessagePolicy.Condition<UnregisteredCompositionCondition>()),
            typeof(OpenCompositionVaultCommand),
            typeof(OpenCompositionVaultCommandHandler));
        var command = new OpenCompositionVaultCommand();

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(command).ConfigureAwait(false);

        result.Outcome.Should().Be(MediationOutcome.Denied);
        result.Code.Should().Be(AuthorizationPolicyRefusalCodes.ConditionMissing);
        AuthorizationPolicyRefusalCodes.ConditionMissing.Should().Be("authorization.condition-missing");
        command.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Builds a provider registering the given types with the policies under test.
    /// </summary>
    /// <param name="recorder">Records the evaluations the guard performs.</param>
    /// <param name="configurePolicies">Collects the policies, conditions, and ports under test.</param>
    /// <param name="types">The command and handler types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(
        CompositionEvaluationRecorder recorder,
        Action<MessagePolicyBuilder> configurePolicies,
        params Type[] types)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
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
