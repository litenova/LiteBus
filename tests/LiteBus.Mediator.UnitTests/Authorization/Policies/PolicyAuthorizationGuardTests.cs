using System;
using System.Security.Claims;
using System.Threading.Tasks;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Messaging.Authorization;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Authorization.Policies;

/// <summary>
///     Verifies that the policy guard runs contributed conditions before the named policy, with the first deny winning.
/// </summary>
[Collection("Sequential")]
public sealed class PolicyAuthorizationGuardTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that an allowed policy runs the handler.
    /// </summary>
    [Fact]
    public async Task An_allowed_policy_runs_the_handler()
    {
        var recorder = new PolicyEvaluationRecorder();
        var provider = BuildProvider(
            recorder,
            new PolicyTestOptions(),
            policies => policies.Add(
                "vaults.open",
                new RecordingPolicy(recorder, "policy:vaults.open", Verdict.Allow)),
            typeof(OpenPolicyVaultCommand),
            typeof(OpenPolicyVaultCommandHandler));
        var command = new OpenPolicyVaultCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
        recorder.Steps.Should().Equal("policy:vaults.open");
    }

    /// <summary>
    ///     Verifies that a denied policy refuses the message with its reason and code.
    /// </summary>
    [Fact]
    public async Task A_denied_policy_refuses_the_message_with_its_reason_and_code()
    {
        var recorder = new PolicyEvaluationRecorder();
        var provider = BuildProvider(
            recorder,
            new PolicyTestOptions(),
            policies => policies.Add("vaults.seal", MessagePolicy.Deny("sealing needs a second approver")),
            typeof(SealPolicyVaultCommand),
            typeof(SealPolicyVaultCommandHandler));
        var command = new SealPolicyVaultCommand();

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Reason.Should().Be("sealing needs a second approver");
        thrown.Which.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
        command.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a declared policy no composition registered denies with the stable code.
    /// </summary>
    [Fact]
    public async Task An_unknown_policy_denies_with_the_stable_code()
    {
        var provider = BuildProvider(
            new PolicyEvaluationRecorder(),
            new PolicyTestOptions(),
            policies => policies.Add("vaults.open", MessagePolicy.Allow()),
            typeof(UnknownPolicyVaultCommand),
            typeof(UnknownPolicyVaultCommandHandler));
        var command = new UnknownPolicyVaultCommand();

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(command).ConfigureAwait(false);

        result.Outcome.Should().Be(MediationOutcome.Denied);
        result.Code.Should().Be(AuthorizationPolicyRefusalCodes.UnknownPolicy);
        result.Reason.Should().Contain("vaults.unmapped");
        AuthorizationPolicyRefusalCodes.UnknownPolicy.Should().Be("authorization.unknown-policy");
        command.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that conditions run before the named policy in registration order.
    /// </summary>
    [Fact]
    public async Task Conditions_run_before_the_named_policy_in_registration_order()
    {
        var recorder = new PolicyEvaluationRecorder();
        var provider = BuildProvider(
            recorder,
            new PolicyTestOptions(),
            policies => policies
                .Add("vaults.open", new RecordingPolicy(recorder, "policy:vaults.open", Verdict.Allow))
                .AddCondition<FirstPolicyCondition>()
                .AddCondition<SecondPolicyCondition>(),
            typeof(OpenPolicyVaultCommand),
            typeof(OpenPolicyVaultCommandHandler));
        var command = new OpenPolicyVaultCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
        recorder.Steps.Should().Equal("first-condition", "second-condition", "policy:vaults.open");
    }

    /// <summary>
    ///     Verifies that a condition denial skips the conditions and policy after it.
    /// </summary>
    [Fact]
    public async Task A_condition_deny_skips_what_follows_it()
    {
        var recorder = new PolicyEvaluationRecorder();
        var provider = BuildProvider(
            recorder,
            new PolicyTestOptions(),
            policies => policies
                .Add("vaults.open", new RecordingPolicy(recorder, "policy:vaults.open", Verdict.Allow))
                .AddCondition<DenyPolicyCondition>()
                .AddCondition<FirstPolicyCondition>(),
            typeof(OpenPolicyVaultCommand),
            typeof(OpenPolicyVaultCommandHandler));
        var command = new OpenPolicyVaultCommand();

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(command).ConfigureAwait(false);

        result.Outcome.Should().Be(MediationOutcome.Denied);
        result.Code.Should().Be("test.condition-deny");
        command.HandlerRan.Should().BeFalse();
        recorder.Steps.Should().Equal("deny-condition");
    }

    /// <summary>
    ///     Verifies that a permission policy grants a held permission.
    /// </summary>
    [Fact]
    public async Task A_permission_policy_grants_a_held_permission()
    {
        var options = new PolicyTestOptions();
        options.Granted.Add("vaults.open");
        var provider = BuildProvider(
            new PolicyEvaluationRecorder(),
            options,
            policies => policies
                .Add("vaults.open", MessagePolicy.Permission("vaults.open"))
                .UsePermissionLookup<FakePermissionLookup>(),
            typeof(OpenPolicyVaultCommand),
            typeof(OpenPolicyVaultCommandHandler));
        var command = new OpenPolicyVaultCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies that a permission policy refuses a missing permission with the stable code.
    /// </summary>
    [Fact]
    public async Task A_permission_policy_refuses_a_missing_permission()
    {
        var provider = BuildProvider(
            new PolicyEvaluationRecorder(),
            new PolicyTestOptions(),
            policies => policies
                .Add("vaults.open", MessagePolicy.Permission("vaults.open"))
                .UsePermissionLookup<FakePermissionLookup>(),
            typeof(OpenPolicyVaultCommand),
            typeof(OpenPolicyVaultCommandHandler));
        var command = new OpenPolicyVaultCommand();

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(command).ConfigureAwait(false);

        result.Outcome.Should().Be(MediationOutcome.Denied);
        result.Code.Should().Be(AuthorizationPolicyRefusalCodes.MissingPermission);
        command.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a permission policy without a lookup denies as unconfigured.
    /// </summary>
    [Fact]
    public async Task A_permission_policy_without_a_lookup_denies_as_unconfigured()
    {
        var provider = BuildProvider(
            new PolicyEvaluationRecorder(),
            new PolicyTestOptions(),
            policies => policies.Add("vaults.open", MessagePolicy.Permission("vaults.open")),
            typeof(OpenPolicyVaultCommand),
            typeof(OpenPolicyVaultCommandHandler));
        var command = new OpenPolicyVaultCommand();

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(command).ConfigureAwait(false);

        result.Outcome.Should().Be(MediationOutcome.Denied);
        result.Code.Should().Be("authorization.permission-lookup-missing");
        command.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that the mediation identity reaches the ownership policy and allows the subject.
    /// </summary>
    [Fact]
    public async Task An_ownership_policy_allows_the_subject()
    {
        var provider = BuildProvider(
            new PolicyEvaluationRecorder(),
            new PolicyTestOptions(),
            policies => policies.Add(
                "vaults.close-account",
                MessagePolicy.Self<ClosePolicyAccountCommand>(message => message.OwnerId)),
            typeof(ClosePolicyAccountCommand),
            typeof(ClosePolicyAccountCommandHandler));
        var command = new ClosePolicyAccountCommand { OwnerId = "subject-1" };
        var settings = new CommandMediationSettings
        {
            Identity = ExecutionIdentity.For(SignedInPrincipal("subject-1"))
        };

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command, settings).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies that the mediation identity reaches the ownership policy and refuses another subject.
    /// </summary>
    [Fact]
    public async Task An_ownership_policy_refuses_another_subject()
    {
        var provider = BuildProvider(
            new PolicyEvaluationRecorder(),
            new PolicyTestOptions(),
            policies => policies.Add(
                "vaults.close-account",
                MessagePolicy.Self<ClosePolicyAccountCommand>(message => message.OwnerId)),
            typeof(ClosePolicyAccountCommand),
            typeof(ClosePolicyAccountCommandHandler));
        var command = new ClosePolicyAccountCommand { OwnerId = "subject-1" };
        var settings = new CommandMediationSettings
        {
            Identity = ExecutionIdentity.For(SignedInPrincipal("subject-2"))
        };

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(command, settings).ConfigureAwait(false);

        result.Outcome.Should().Be(MediationOutcome.Denied);
        result.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
        command.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that an exempt message is allowed without evaluating policies.
    /// </summary>
    [Fact]
    public async Task An_exempt_message_is_allowed_without_evaluating_policies()
    {
        var recorder = new PolicyEvaluationRecorder();
        var provider = BuildProvider(
            recorder,
            new PolicyTestOptions(),
            policies => policies.Add(
                "vaults.open",
                new RecordingPolicy(recorder, "policy:vaults.open", Verdict.Allow)),
            typeof(BrowsePolicyVaultCommand),
            typeof(BrowsePolicyVaultCommandHandler));
        var command = new BrowsePolicyVaultCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
        recorder.Steps.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that an undeclared message is denied with the stable code.
    /// </summary>
    [Fact]
    public async Task An_undeclared_message_is_denied_with_the_stable_code()
    {
        var provider = BuildProvider(
            new PolicyEvaluationRecorder(),
            new PolicyTestOptions(),
            policies => policies.Add("vaults.open", MessagePolicy.Allow()),
            typeof(DraftPolicyVaultCommand),
            typeof(DraftPolicyVaultCommandHandler));
        var command = new DraftPolicyVaultCommand();

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Code.Should().Be("declaration.missing");
        command.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Builds a provider registering the given types with the recorder, options, and policies under test.
    /// </summary>
    /// <param name="recorder">Records the evaluations the guard performs.</param>
    /// <param name="options">The permissions the fake lookup grants.</param>
    /// <param name="configurePolicies">Collects the policies, conditions, and lookup under test.</param>
    /// <param name="types">The command and handler types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(
        PolicyEvaluationRecorder recorder,
        PolicyTestOptions options,
        Action<MessagePolicyBuilder> configurePolicies,
        params Type[] types)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddSingleton(options);

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

    private static ClaimsPrincipal SignedInPrincipal(string subjectId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subjectId)], "test"));
    }
}
