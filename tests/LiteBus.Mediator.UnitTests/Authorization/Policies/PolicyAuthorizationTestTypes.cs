using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Authorization;

namespace LiteBus.Mediator.UnitTests.Authorization.Policies;

/// <summary>
///     Records the authorization evaluations a mediation performs, in observation order.
/// </summary>
internal sealed class PolicyEvaluationRecorder
{
    /// <summary>
    ///     Gets the evaluation steps observed so far, in observation order.
    /// </summary>
    public List<string> Steps { get; } = [];
}

/// <summary>
///     The permissions the fake lookup grants, shared with the composition under test.
/// </summary>
internal sealed class PolicyTestOptions
{
    /// <summary>
    ///     Gets the permissions the fake lookup grants.
    /// </summary>
    public HashSet<string> Granted { get; } = new(StringComparer.Ordinal);
}

/// <summary>
///     Grants the permissions <see cref="PolicyTestOptions" /> holds.
/// </summary>
internal sealed class FakePermissionLookup : IPermissionLookup
{
    /// <summary>
    ///     The permissions this lookup grants.
    /// </summary>
    private readonly PolicyTestOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakePermissionLookup" /> class.
    /// </summary>
    /// <param name="options">The permissions this lookup grants.</param>
    public FakePermissionLookup(PolicyTestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <inheritdoc />
    public ValueTask<bool> HasPermissionAsync(
        ExecutionIdentity identity,
        string permission,
        AuthorizationContext context,
        CancellationToken cancellationToken)
    {
        return new ValueTask<bool>(_options.Granted.Contains(permission));
    }
}

/// <summary>
///     A condition that records its evaluation as the first step and allows.
/// </summary>
internal sealed class FirstPolicyCondition : IAuthorizationCondition
{
    /// <summary>
    ///     Records the evaluations the guard performs.
    /// </summary>
    private readonly PolicyEvaluationRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FirstPolicyCondition" /> class.
    /// </summary>
    /// <param name="recorder">Records the evaluations the guard performs.</param>
    public FirstPolicyCondition(PolicyEvaluationRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        _recorder = recorder;
    }

    /// <inheritdoc />
    public ValueTask<Verdict> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        _recorder.Steps.Add("first-condition");

        return new ValueTask<Verdict>(Verdict.Allow);
    }
}

/// <summary>
///     A condition that records its evaluation as the second step and allows.
/// </summary>
internal sealed class SecondPolicyCondition : IAuthorizationCondition
{
    /// <summary>
    ///     Records the evaluations the guard performs.
    /// </summary>
    private readonly PolicyEvaluationRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SecondPolicyCondition" /> class.
    /// </summary>
    /// <param name="recorder">Records the evaluations the guard performs.</param>
    public SecondPolicyCondition(PolicyEvaluationRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        _recorder = recorder;
    }

    /// <inheritdoc />
    public ValueTask<Verdict> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        _recorder.Steps.Add("second-condition");

        return new ValueTask<Verdict>(Verdict.Allow);
    }
}

/// <summary>
///     A condition that records its evaluation and denies.
/// </summary>
internal sealed class DenyPolicyCondition : IAuthorizationCondition
{
    /// <summary>
    ///     Records the evaluations the guard performs.
    /// </summary>
    private readonly PolicyEvaluationRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DenyPolicyCondition" /> class.
    /// </summary>
    /// <param name="recorder">Records the evaluations the guard performs.</param>
    public DenyPolicyCondition(PolicyEvaluationRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        _recorder = recorder;
    }

    /// <inheritdoc />
    public ValueTask<Verdict> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        _recorder.Steps.Add("deny-condition");

        return new ValueTask<Verdict>(Verdict.Deny(
            "the deny condition refused the message.",
            "test.condition-deny"));
    }
}

/// <summary>
///     A policy that records its evaluation and answers a fixed verdict.
/// </summary>
/// <param name="Recorder">Records the evaluations the guard performs.</param>
/// <param name="Name">The step recorded when this policy evaluates.</param>
/// <param name="Result">The verdict this policy answers.</param>
internal sealed record RecordingPolicy(PolicyEvaluationRecorder Recorder, string Name, Verdict Result) : MessagePolicy
{
    /// <inheritdoc />
    public override ValueTask<Verdict> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        Recorder.Steps.Add(Name);

        return new ValueTask<Verdict>(Result);
    }
}

/// <summary>
///     A message carrying the subject an ownership policy reads.
/// </summary>
/// <param name="SubjectId">The owning subject identifier, or <see langword="null" /> when the message names none.</param>
internal sealed record PolicySubjectMessage(string? SubjectId);

/// <summary>
///     A message of another type, used to assert an ownership policy refuses what it does not apply to.
/// </summary>
internal sealed record PolicyOtherMessage;

/// <summary>
///     A command whose authorization position requires the vaults.open policy.
/// </summary>
[Authorized("vaults.open")]
internal sealed class OpenPolicyVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="OpenPolicyVaultCommand" />.
/// </summary>
internal sealed class OpenPolicyVaultCommandHandler : ICommandHandler<OpenPolicyVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(OpenPolicyVaultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose authorization position requires the vaults.seal policy.
/// </summary>
[Authorized("vaults.seal")]
internal sealed class SealPolicyVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="SealPolicyVaultCommand" />.
/// </summary>
internal sealed class SealPolicyVaultCommandHandler : ICommandHandler<SealPolicyVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(SealPolicyVaultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose authorization position names a policy no composition registers.
/// </summary>
[Authorized("vaults.unmapped")]
internal sealed class UnknownPolicyVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="UnknownPolicyVaultCommand" />.
/// </summary>
internal sealed class UnknownPolicyVaultCommandHandler : ICommandHandler<UnknownPolicyVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(UnknownPolicyVaultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command that records why it needs no authorization rather than omitting a position.
/// </summary>
[AuthorizationExempt("the vault catalogue is public, so there is no actor to authorize")]
internal sealed class BrowsePolicyVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="BrowsePolicyVaultCommand" />.
/// </summary>
internal sealed class BrowsePolicyVaultCommandHandler : ICommandHandler<BrowsePolicyVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(BrowsePolicyVaultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command declaring no authorization position, used to assert the undeclared decision.
/// </summary>
internal sealed class DraftPolicyVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="DraftPolicyVaultCommand" />.
/// </summary>
internal sealed class DraftPolicyVaultCommandHandler : ICommandHandler<DraftPolicyVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(DraftPolicyVaultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command naming the account it closes, used to assert an ownership policy.
/// </summary>
[Authorized("vaults.close-account")]
internal sealed class ClosePolicyAccountCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the account the command closes.
    /// </summary>
    public string? OwnerId { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="ClosePolicyAccountCommand" />.
/// </summary>
internal sealed class ClosePolicyAccountCommandHandler : ICommandHandler<ClosePolicyAccountCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(ClosePolicyAccountCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}
