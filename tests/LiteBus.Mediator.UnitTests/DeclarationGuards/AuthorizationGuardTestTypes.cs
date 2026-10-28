using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.DeclarationGuards;

/// <summary>
///     Records the policies the authorization guards are asked to enforce.
/// </summary>
internal sealed class AuthorizationRecorder
{
    /// <summary>
    ///     Gets the policies passed to the authorization check, in observation order.
    /// </summary>
    public List<string> Policies { get; } = [];
}

/// <summary>
///     Decides which policies the calling actor satisfies.
/// </summary>
internal sealed class FakeAccessAuthorizer
{
    /// <summary>
    ///     The policies the actor satisfies.
    /// </summary>
    private readonly HashSet<string> _held;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeAccessAuthorizer" /> class.
    /// </summary>
    /// <param name="held">The policies the actor satisfies.</param>
    public FakeAccessAuthorizer(params string[] held)
    {
        _held = [.. held];
    }

    /// <summary>
    ///     Determines whether the actor satisfies the given policy.
    /// </summary>
    /// <param name="policy">The policy to check.</param>
    /// <returns><see langword="true" /> when the actor satisfies it.</returns>
    public bool Holds(string policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return _held.Contains(policy);
    }
}

/// <summary>
///     Records the policy it receives and enforces it against the access authorizer.
/// </summary>
/// <typeparam name="TCommand">The command type the guard is closed over at registration.</typeparam>
internal sealed class StubAuthorizationGuard<TCommand> : AuthorizationGuard<TCommand>
    where TCommand : ICommand
{
    /// <summary>
    ///     Decides which policies the calling actor satisfies.
    /// </summary>
    private readonly FakeAccessAuthorizer _authorizer;

    /// <summary>
    ///     Records the policies the guard observes.
    /// </summary>
    private readonly AuthorizationRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StubAuthorizationGuard{TCommand}" /> class.
    /// </summary>
    /// <param name="metadata">Reads the declaration from the message type.</param>
    /// <param name="recorder">Records the policies the guard observes.</param>
    /// <param name="authorizer">Decides which policies the calling actor satisfies.</param>
    public StubAuthorizationGuard(IMessageMetadataAccessor metadata, AuthorizationRecorder recorder, FakeAccessAuthorizer authorizer)
        : base(metadata)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(authorizer);

        _recorder = recorder;
        _authorizer = authorizer;
    }

    /// <inheritdoc />
    protected override Task<Verdict> AuthorizeAsync(string policy, TCommand message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(message);

        _recorder.Policies.Add(policy);

        return Task.FromResult(_authorizer.Holds(policy)
            ? Verdict.Allow
            : Verdict.Deny($"the caller is not authorized under {policy}", "test.forbidden"));
    }
}

/// <summary>
///     A command whose authorization position requires the vaults.open policy.
/// </summary>
[Authorized("vaults.open")]
internal sealed class OpenGuardedVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="OpenGuardedVaultCommand" />.
/// </summary>
internal sealed class OpenGuardedVaultCommandHandler : ICommandHandler<OpenGuardedVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(OpenGuardedVaultCommand message, CancellationToken cancellationToken = default)
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
internal sealed class SealGuardedVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="SealGuardedVaultCommand" />.
/// </summary>
internal sealed class SealGuardedVaultCommandHandler : ICommandHandler<SealGuardedVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(SealGuardedVaultCommand message, CancellationToken cancellationToken = default)
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
internal sealed class BrowseGuardedVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="BrowseGuardedVaultCommand" />.
/// </summary>
internal sealed class BrowseGuardedVaultCommandHandler : ICommandHandler<BrowseGuardedVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(BrowseGuardedVaultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command declaring no authorization position, used to assert the undeclared decision.
/// </summary>
internal sealed class DraftGuardedVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="DraftGuardedVaultCommand" />.
/// </summary>
internal sealed class DraftGuardedVaultCommandHandler : ICommandHandler<DraftGuardedVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(DraftGuardedVaultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}
