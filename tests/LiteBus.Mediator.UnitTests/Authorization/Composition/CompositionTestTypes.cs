using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Authorization;

namespace LiteBus.Mediator.UnitTests.Authorization.Composition;

/// <summary>
///     Records the authorization evaluations a mediation performs, in observation order.
/// </summary>
internal sealed class CompositionEvaluationRecorder
{
    /// <summary>
    ///     Gets the evaluation steps observed so far, in observation order.
    /// </summary>
    public List<string> Steps { get; } = [];
}

/// <summary>
///     A policy that records its evaluation and answers a fixed result.
/// </summary>
/// <param name="Recorder">Records the evaluations the guard performs.</param>
/// <param name="Name">The step recorded when this policy evaluates.</param>
/// <param name="Result">The result this policy answers.</param>
internal sealed record RecordingCompositionPolicy(CompositionEvaluationRecorder Recorder, string Name, PolicyResult Result) : MessagePolicy
{
    /// <inheritdoc />
    public override ValueTask<PolicyResult> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        Recorder.Steps.Add(Name);

        return new ValueTask<PolicyResult>(Result);
    }
}

/// <summary>
///     A condition that records its evaluation as the first step and allows.
/// </summary>
internal sealed class FirstCompositionCondition : IAuthorizationCondition
{
    /// <summary>
    ///     Records the evaluations the guard performs.
    /// </summary>
    private readonly CompositionEvaluationRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FirstCompositionCondition" /> class.
    /// </summary>
    /// <param name="recorder">Records the evaluations the guard performs.</param>
    public FirstCompositionCondition(CompositionEvaluationRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        _recorder = recorder;
    }

    /// <inheritdoc />
    public ValueTask<PolicyResult> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        _recorder.Steps.Add("first-condition");

        return new ValueTask<PolicyResult>(PolicyResult.Allow);
    }
}

/// <summary>
///     A condition that records its evaluation as the second step and allows.
/// </summary>
internal sealed class SecondCompositionCondition : IAuthorizationCondition
{
    /// <summary>
    ///     Records the evaluations the guard performs.
    /// </summary>
    private readonly CompositionEvaluationRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SecondCompositionCondition" /> class.
    /// </summary>
    /// <param name="recorder">Records the evaluations the guard performs.</param>
    public SecondCompositionCondition(CompositionEvaluationRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        _recorder = recorder;
    }

    /// <inheritdoc />
    public ValueTask<PolicyResult> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        _recorder.Steps.Add("second-condition");

        return new ValueTask<PolicyResult>(PolicyResult.Allow);
    }
}

/// <summary>
///     A policy-scoped condition that records its evaluation and allows.
/// </summary>
internal sealed class NamedCompositionCondition : IAuthorizationCondition
{
    /// <summary>
    ///     Records the evaluations the guard performs.
    /// </summary>
    private readonly CompositionEvaluationRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="NamedCompositionCondition" /> class.
    /// </summary>
    /// <param name="recorder">Records the evaluations the guard performs.</param>
    public NamedCompositionCondition(CompositionEvaluationRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        _recorder = recorder;
    }

    /// <inheritdoc />
    public ValueTask<PolicyResult> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        _recorder.Steps.Add("named-condition");

        return new ValueTask<PolicyResult>(PolicyResult.Allow);
    }
}

/// <summary>
///     A policy-scoped condition no composition registers, used to assert the missing-registration denial.
/// </summary>
internal sealed class UnregisteredCompositionCondition : IAuthorizationCondition
{
    /// <inheritdoc />
    public ValueTask<PolicyResult> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        return new ValueTask<PolicyResult>(PolicyResult.Allow);
    }
}

/// <summary>
///     The permissions the granting lookup grants, shared with the composition under test.
/// </summary>
internal sealed class CompositionPermissionOptions
{
    /// <summary>
    ///     Gets the permissions the granting lookup grants.
    /// </summary>
    public HashSet<string> Granted { get; } = new(StringComparer.Ordinal);
}

/// <summary>
///     Grants the permissions <see cref="CompositionPermissionOptions" /> holds.
/// </summary>
internal sealed class GrantingCompositionLookup : IPermissionLookup
{
    /// <summary>
    ///     The permissions this lookup grants.
    /// </summary>
    private readonly CompositionPermissionOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="GrantingCompositionLookup" /> class.
    /// </summary>
    /// <param name="options">The permissions this lookup grants.</param>
    public GrantingCompositionLookup(CompositionPermissionOptions options)
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
///     Denies every permission, used to assert that a second lookup fails composition.
/// </summary>
internal sealed class AlternateCompositionLookup : IPermissionLookup
{
    /// <inheritdoc />
    public ValueTask<bool> HasPermissionAsync(
        ExecutionIdentity identity,
        string permission,
        AuthorizationContext context,
        CancellationToken cancellationToken)
    {
        return new ValueTask<bool>(false);
    }
}

/// <summary>
///     Answers the handled instant as the sign-in instant, so a recent sign-in always passes.
/// </summary>
internal sealed class HandledAtCompositionSource : ISignInInstantSource
{
    /// <inheritdoc />
    public ValueTask<DateTimeOffset?> GetSignInInstantAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new ValueTask<DateTimeOffset?>(context.HandledAt);
    }
}

/// <summary>
///     Answers no sign-in instant, used to assert that a second source fails composition.
/// </summary>
internal sealed class NullCompositionSource : ISignInInstantSource
{
    /// <inheritdoc />
    public ValueTask<DateTimeOffset?> GetSignInInstantAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        return new ValueTask<DateTimeOffset?>((DateTimeOffset?) null);
    }
}

/// <summary>
///     A command whose authorization position requires the vaults.open policy.
/// </summary>
[Authorized("vaults.open")]
internal sealed class OpenCompositionVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="OpenCompositionVaultCommand" />.
/// </summary>
internal sealed class OpenCompositionVaultCommandHandler : ICommandHandler<OpenCompositionVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(OpenCompositionVaultCommand message, CancellationToken cancellationToken = default)
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
internal sealed class SealCompositionVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="SealCompositionVaultCommand" />.
/// </summary>
internal sealed class SealCompositionVaultCommandHandler : ICommandHandler<SealCompositionVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(SealCompositionVaultCommand message, CancellationToken cancellationToken = default)
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
internal sealed class UnmappedCompositionVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="UnmappedCompositionVaultCommand" />.
/// </summary>
internal sealed class UnmappedCompositionVaultCommandHandler : ICommandHandler<UnmappedCompositionVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(UnmappedCompositionVaultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A second command naming the same unregistered policy, used to assert every message is listed.
/// </summary>
[Authorized("vaults.unmapped")]
internal sealed class RekeyCompositionVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="RekeyCompositionVaultCommand" />.
/// </summary>
internal sealed class RekeyCompositionVaultCommandHandler : ICommandHandler<RekeyCompositionVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(RekeyCompositionVaultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command naming another unregistered policy, used to assert every unknown name is listed.
/// </summary>
[Authorized("vaults.audit")]
internal sealed class AuditCompositionVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="AuditCompositionVaultCommand" />.
/// </summary>
internal sealed class AuditCompositionVaultCommandHandler : ICommandHandler<AuditCompositionVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(AuditCompositionVaultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     An event declaring no authorization position, used to assert the module default exempts it.
/// </summary>
internal sealed class VaultOpenedEvent : IEvent
{
    /// <summary>
    ///     Gets or sets a value indicating whether the event handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="VaultOpenedEvent" />.
/// </summary>
internal sealed class VaultOpenedEventHandler : IEventHandler<VaultOpenedEvent>
{
    /// <inheritdoc />
    public Task HandleAsync(VaultOpenedEvent message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     An event whose authorization position requires the vaults.open policy.
/// </summary>
[Authorized("vaults.open")]
internal sealed class RestrictedVaultOpenedEvent : IEvent
{
    /// <summary>
    ///     Gets or sets a value indicating whether the event handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="RestrictedVaultOpenedEvent" />.
/// </summary>
internal sealed class RestrictedVaultOpenedEventHandler : IEventHandler<RestrictedVaultOpenedEvent>
{
    /// <inheritdoc />
    public Task HandleAsync(RestrictedVaultOpenedEvent message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     An event that records why it needs no authorization rather than relying on the module default.
/// </summary>
[AuthorizationExempt("the archive notice is public, so there is no actor to authorize")]
internal sealed class PublicVaultOpenedEvent : IEvent
{
    /// <summary>
    ///     Gets or sets a value indicating whether the event handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="PublicVaultOpenedEvent" />.
/// </summary>
internal sealed class PublicVaultOpenedEventHandler : IEventHandler<PublicVaultOpenedEvent>
{
    /// <inheritdoc />
    public Task HandleAsync(PublicVaultOpenedEvent message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}
