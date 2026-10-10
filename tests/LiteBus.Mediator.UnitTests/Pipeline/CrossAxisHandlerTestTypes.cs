using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Records which messages the shared guard decided on, so one implementation can be shown to cover both axes.
/// </summary>
public sealed class CrossAxisRecorder
{
    /// <summary>
    ///     Gets the message type names the guard saw, in order.
    /// </summary>
    public List<string> Seen { get; } = [];

    /// <summary>
    ///     Gets or sets the message type name the guard denies.
    /// </summary>
    public string? DenyFor { get; set; }
}

/// <summary>
///     One authorization guard written against the messaging-level contract and constrained to commands.
/// </summary>
/// <typeparam name="TMessage">The command type, bound at registration.</typeparam>
/// <remarks>
///     It exists to prove a handler written against the messaging-level contract registers on an axis. Before this,
///     covering commands and queries meant two near-identical classes, and the duplicated code was authorization.
/// </remarks>
internal sealed class SharedCommandGuard<TMessage> : IMessageGuard<TMessage>
    where TMessage : ICommand
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CrossAxisRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SharedCommandGuard{TMessage}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public SharedCommandGuard(CrossAxisRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<Verdict> DecideAsync(TMessage message, CancellationToken cancellationToken = default)
    {
        _recorder.Seen.Add(typeof(TMessage).Name);

        return Task.FromResult(_recorder.DenyFor == typeof(TMessage).Name
            ? Verdict.Deny("not permitted", code: "NOT_PERMITTED")
            : Verdict.Allow);
    }
}

/// <summary>
///     The same guard shape constrained to queries, standing in for the second registration of one implementation.
/// </summary>
/// <typeparam name="TMessage">The query type, bound at registration.</typeparam>
internal sealed class SharedQueryGuard<TMessage> : IMessageGuard<TMessage>
    where TMessage : IQuery
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CrossAxisRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SharedQueryGuard{TMessage}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public SharedQueryGuard(CrossAxisRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<Verdict> DecideAsync(TMessage message, CancellationToken cancellationToken = default)
    {
        _recorder.Seen.Add(typeof(TMessage).Name);

        return Task.FromResult(_recorder.DenyFor == typeof(TMessage).Name
            ? Verdict.Deny("not permitted", code: "NOT_PERMITTED")
            : Verdict.Allow);
    }
}

/// <summary>
///     A guard written against the messaging-level contract with no axis constraint, which no axis may accept.
/// </summary>
/// <typeparam name="TMessage">An unconstrained message type.</typeparam>
internal sealed class UnconstrainedGuard<TMessage> : IMessageGuard<TMessage>
    where TMessage : notnull
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(TMessage message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Verdict.Allow);
    }
}

/// <summary>
///     A command the shared guard runs for.
/// </summary>
internal sealed class ApproveLeaveCommand : ICommand;

/// <summary>
///     Handles <see cref="ApproveLeaveCommand" />.
/// </summary>
internal sealed class ApproveLeaveCommandHandler : ICommandHandler<ApproveLeaveCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(ApproveLeaveCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A query the shared guard runs for.
/// </summary>
internal sealed class ListLeaveQuery : IQuery<int>;

/// <summary>
///     Handles <see cref="ListLeaveQuery" />.
/// </summary>
internal sealed class ListLeaveQueryHandler : IQueryHandler<ListLeaveQuery, int>
{
    /// <inheritdoc />
    public Task<int> HandleAsync(ListLeaveQuery message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(3);
    }
}

/// <summary>
///     An audit trail that discards what it is given, for tests that only assert on composition.
/// </summary>
internal sealed class NullAuditTrail : IAuditTrail
{
    /// <inheritdoc />
    public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
