using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Auditing;

/// <summary>
///     A command that names the account acting, which is what the resolver reads.
/// </summary>
internal interface IActingAccountCommand
{
    /// <summary>
    ///     Gets the account performing the action.
    /// </summary>
    string ActingAccountId { get; }
}

/// <summary>
///     An audited command carrying an acting account, so the record can be attributed.
/// </summary>
[Audited("accounts.close-account", Category = "accounts", TargetKind = "account")]
internal sealed class CloseAccountCommand : ICommand, IActingAccountCommand
{
    /// <inheritdoc />
    public string ActingAccountId { get; set; } = "acct-1";

    /// <summary>
    ///     Gets or sets a value indicating whether the guard denies the command.
    /// </summary>
    public bool ShouldDeny { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the handler attributes the action itself.
    /// </summary>
    public bool HandlerAttributes { get; set; }
}

/// <summary>
///     Denies <see cref="CloseAccountCommand" /> on request, so the test can prove attribution survives a denial.
/// </summary>
internal sealed class CloseAccountCommandGuard : ICommandGuard<CloseAccountCommand>
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(CloseAccountCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.ShouldDeny
            ? Task.FromResult(Verdict.Deny("not permitted to close this account", code: "NOT_PERMITTED"))
            : Task.FromResult(Verdict.Allow);
    }
}

/// <summary>
///     Handles <see cref="CloseAccountCommand" />, optionally overriding the resolved actor.
/// </summary>
internal sealed class CloseAccountCommandHandler : ICommandHandler<CloseAccountCommand>
{
    /// <summary>
    ///     The audit scope used to override attribution.
    /// </summary>
    private readonly IAuditScope _audit;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CloseAccountCommandHandler" /> class.
    /// </summary>
    /// <param name="audit">The audit scope resolved from the container.</param>
    public CloseAccountCommandHandler(IAuditScope audit)
    {
        _audit = audit;
    }

    /// <inheritdoc />
    public Task HandleAsync(CloseAccountCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.HandlerAttributes)
        {
            _audit.WithActor(AuditActor.User("acct-override", "Override Person"));
        }

        return Task.CompletedTask;
    }
}

/// <summary>
///     An audited command with no acting account, standing in for one a scheduled worker raises.
/// </summary>
[Audited("accounts.expire-sessions", Category = "accounts")]
internal sealed class ExpireSessionsCommand : ICommand;

/// <summary>
///     Handles <see cref="ExpireSessionsCommand" />.
/// </summary>
internal sealed class ExpireSessionsCommandHandler : ICommandHandler<ExpireSessionsCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(ExpireSessionsCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     An audited domain fact, so the event axis can be shown to write records.
/// </summary>
[Audited("accounts.account-closed", Category = "accounts", TargetKind = "account")]
internal sealed class AccountClosedEvent : IEvent;

/// <summary>
///     One of two reactions to <see cref="AccountClosedEvent" />, proving a publish writes one record and not one per
///     handler.
/// </summary>
internal sealed class NotifyOnAccountClosed : IEventHandler<AccountClosedEvent>
{
    /// <inheritdoc />
    public Task HandleAsync(AccountClosedEvent message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     The second reaction to <see cref="AccountClosedEvent" />.
/// </summary>
internal sealed class ArchiveOnAccountClosed : IEventHandler<AccountClosedEvent>
{
    /// <inheritdoc />
    public Task HandleAsync(AccountClosedEvent message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     Attributes an action from the message, the way an application resolver does.
/// </summary>
/// <remarks>
///     It reads the acting account off the message when there is one and names the process otherwise, which is the
///     three-line shape the resolver exists to make possible. Running at the completion stage is what lets it attribute
///     a denied command; a pre-stage handler would never run on that path.
/// </remarks>
internal sealed class TestAuditActorResolver : IAuditActorResolver
{
    /// <inheritdoc />
    public AuditActor? Resolve(MessageCompletionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Message switch
        {
            IActingAccountCommand acting => AuditActor.User(acting.ActingAccountId, "Acting Person"),
            AccountClosedEvent => AuditActor.System("account-closed-reaction"),
            _ => AuditActor.System("scheduled-worker")
        };
    }
}

/// <summary>
///     Resolves no actor at all, standing in for a host where attribution is genuinely unavailable.
/// </summary>
internal sealed class UnattributedAuditActorResolver : IAuditActorResolver
{
    /// <inheritdoc />
    public AuditActor? Resolve(MessageCompletionContext context)
    {
        return null;
    }
}
