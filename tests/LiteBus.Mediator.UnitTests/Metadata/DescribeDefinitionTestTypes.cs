using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.Metadata;

/// <summary>
///     A command carrying an acting account, used as the marker a scoped requirement is keyed on.
/// </summary>
internal interface IAttributedCommand
{
    /// <summary>
    ///     Gets the account performing the action.
    /// </summary>
    string ActingAccountId { get; }
}

/// <summary>
///     The permission a command declares it needs, standing in for an application's own declaration type.
/// </summary>
/// <param name="Name">The permission name.</param>
internal sealed record RequiredAuthorization(string Name);

/// <summary>
///     A command that declares two values, which is the case the describe shape exists for.
/// </summary>
internal sealed class TransferFundsCommand : ICommand, IAttributedCommand
{
    /// <inheritdoc />
    public string ActingAccountId { get; set; } = "acct-1";
}

/// <summary>
///     Handles <see cref="TransferFundsCommand" />.
/// </summary>
internal sealed class TransferFundsCommandHandler : ICommandHandler<TransferFundsCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(TransferFundsCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     Declares both of <see cref="TransferFundsCommand" />'s positions from one method.
/// </summary>
/// <remarks>
///     Under the keyed shape the second declaration would have to be an explicit interface implementation naming the
///     message type and the value type again, which is the ergonomic cost this shape removes.
/// </remarks>
internal sealed class TransferFundsCommandDefinition : IMessageDefinition<TransferFundsCommand>
{
    /// <inheritdoc />
    public void Describe(IMessageDeclarations declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        declarations.Audited("money.transfer-funds", category: "money", targetKind: "account");
        declarations.Declare(new RequiredAuthorization("money.transfer"));
    }
}

/// <summary>
///     A command that describes itself as deliberately unaudited.
/// </summary>
internal sealed class PingCommand : ICommand;

/// <summary>
///     Handles <see cref="PingCommand" />.
/// </summary>
internal sealed class PingCommandHandler : ICommandHandler<PingCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(PingCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     Records both exemptions <see cref="PingCommand" /> relies on.
/// </summary>
internal sealed class PingCommandDefinition : IMessageDefinition<PingCommand>
{
    /// <inheritdoc />
    public void Describe(IMessageDeclarations declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        declarations.NotAudited("a liveness probe is not a business action");
        declarations.Exempt<RequiredAuthorization>("the probe is unauthenticated by design");
    }
}

/// <summary>
///     A query with no declarations at all, used to prove a command-scoped requirement leaves queries alone.
/// </summary>
internal sealed class ListAccountsQuery : IQuery<int>;

/// <summary>
///     Handles <see cref="ListAccountsQuery" />.
/// </summary>
internal sealed class ListAccountsQueryHandler : IQueryHandler<ListAccountsQuery, int>
{
    /// <inheritdoc />
    public Task<int> HandleAsync(ListAccountsQuery message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(1);
    }
}

/// <summary>
///     A command with no acting account, so a marker-scoped requirement does not reach it.
/// </summary>
internal sealed class SweepStaleLocksCommand : ICommand;

/// <summary>
///     Handles <see cref="SweepStaleLocksCommand" />.
/// </summary>
internal sealed class SweepStaleLocksCommandHandler : ICommandHandler<SweepStaleLocksCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(SweepStaleLocksCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command that declares the same value type twice, which is a typo the collector reports.
/// </summary>
internal sealed class DoubleDescribedCommand : ICommand;

/// <summary>
///     Handles <see cref="DoubleDescribedCommand" />.
/// </summary>
internal sealed class DoubleDescribedCommandHandler : ICommandHandler<DoubleDescribedCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(DoubleDescribedCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     Declares one value type twice, so the second would silently replace the first.
/// </summary>
internal sealed class DoubleDescribedCommandDefinition : IMessageDefinition<DoubleDescribedCommand>
{
    /// <inheritdoc />
    public void Describe(IMessageDeclarations declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        declarations.Declare(new RequiredAuthorization("first"));
        declarations.Declare(new RequiredAuthorization("second"));
    }
}

/// <summary>
///     Declares nothing, which is indistinguishable from a definition nobody finished.
/// </summary>
internal sealed class EmptyDescribedCommandDefinition : IMessageDefinition<DoubleDescribedCommand>
{
    /// <inheritdoc />
    public void Describe(IMessageDeclarations declarations)
    {
    }
}

/// <summary>
///     A command whose audit action breaks the house naming convention.
/// </summary>
[Audited("Money_TransferFundsBadly")]
internal sealed class BadlyNamedCommand : ICommand;

/// <summary>
///     Handles <see cref="BadlyNamedCommand" />.
/// </summary>
internal sealed class BadlyNamedCommandHandler : ICommandHandler<BadlyNamedCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(BadlyNamedCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command declaring the same audit action another command declares.
/// </summary>
[Audited("money.transfer-funds")]
internal sealed class ClashingActionCommand : ICommand;

/// <summary>
///     Handles <see cref="ClashingActionCommand" />.
/// </summary>
internal sealed class ClashingActionCommandHandler : ICommandHandler<ClashingActionCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(ClashingActionCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command carrying an acting account but declaring no permission, so a marker-scoped requirement reports it.
/// </summary>
internal sealed class UnattributedTransferCommand : ICommand, IAttributedCommand
{
    /// <inheritdoc />
    public string ActingAccountId { get; set; } = "acct-2";
}

/// <summary>
///     Handles <see cref="UnattributedTransferCommand" />.
/// </summary>
internal sealed class UnattributedTransferCommandHandler : ICommandHandler<UnattributedTransferCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(UnattributedTransferCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A retention rule declared for a whole axis, standing in for a value no message states individually.
/// </summary>
/// <param name="Days">How long the message's records are kept.</param>
internal sealed record RetentionWindow(int Days);
