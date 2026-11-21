using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Tenancy;

namespace LiteBus.Mediator.UnitTests.Tenancy;

/// <summary>
///     Carries a chosen execution identity as the ambient context of a tenancy guard decision.
/// </summary>
internal sealed class TestExecutionContext : IExecutionContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TestExecutionContext" /> class.
    /// </summary>
    /// <param name="identity">The identity the guarded decision runs for.</param>
    public TestExecutionContext(ExecutionIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Identity = identity;
    }

    /// <inheritdoc />
    public CancellationToken CancellationToken => CancellationToken.None;

    /// <inheritdoc />
    public DateTimeOffset HandledAt { get; } = DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public ExecutionIdentity Identity { get; }

    /// <inheritdoc />
    public IDictionary<string, object> Items { get; } = new Dictionary<string, object>();

    /// <inheritdoc />
    public IHandleContextData Data { get; } = new HandleContextData();

    /// <inheritdoc />
    public IReadOnlyCollection<string> Tags { get; } = [];

    /// <inheritdoc />
    public object? MessageResult { get; set; }

    /// <inheritdoc />
    public bool PostHandlersSuppressed { get; private set; }

    /// <inheritdoc />
    public void SuppressPostHandlers()
    {
        PostHandlersSuppressed = true;
    }
}

/// <summary>
///     A command whose tenancy position requires a tenant, declared with an attribute.
/// </summary>
[TenantRequired]
internal sealed class PostWorkspaceEntryCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="PostWorkspaceEntryCommand" />.
/// </summary>
internal sealed class PostWorkspaceEntryCommandHandler : ICommandHandler<PostWorkspaceEntryCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(PostWorkspaceEntryCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose tenancy position runs across tenants, declared with an attribute.
/// </summary>
[CrossTenant("the retention job purges expired entries across every workspace")]
internal sealed class PurgeExpiredEntriesCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="PurgeExpiredEntriesCommand" />.
/// </summary>
internal sealed class PurgeExpiredEntriesCommandHandler : ICommandHandler<PurgeExpiredEntriesCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(PurgeExpiredEntriesCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose tenancy position has no tenant, declared with an attribute.
/// </summary>
[NoTenant("sign-in creates the session, so there is no tenant yet")]
internal sealed class SignInCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="SignInCommand" />.
/// </summary>
internal sealed class SignInCommandHandler : ICommandHandler<SignInCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(SignInCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose tenancy position is declared in a keyed definition class.
/// </summary>
internal sealed class RotateWorkspaceKeyCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="RotateWorkspaceKeyCommand" />.
/// </summary>
internal sealed class RotateWorkspaceKeyCommandHandler : ICommandHandler<RotateWorkspaceKeyCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(RotateWorkspaceKeyCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     Declares the tenancy position of <see cref="RotateWorkspaceKeyCommand" />.
/// </summary>
internal sealed class RotateWorkspaceKeyCommandTenancyDefinition : ITenancyDefinition<RotateWorkspaceKeyCommand>
{
    /// <inheritdoc />
    public TenancyDeclaration Tenancy => TenancyDeclaration.Required();
}

/// <summary>
///     A command declaring no tenancy position, used to assert the undeclared decision and the requirement.
/// </summary>
internal sealed class DraftWorkspaceNoticeCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="DraftWorkspaceNoticeCommand" />.
/// </summary>
internal sealed class DraftWorkspaceNoticeCommandHandler : ICommandHandler<DraftWorkspaceNoticeCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(DraftWorkspaceNoticeCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}
