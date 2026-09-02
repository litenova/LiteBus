using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Records what a typed generic post-handler saw, so the test can assert it read a typed result.
/// </summary>
public sealed class TypedResultRecorder
{
    /// <summary>
    ///     Gets the results observed, rendered as type and value.
    /// </summary>
    public List<string> Seen { get; } = [];
}

/// <summary>
///     The result type <see cref="IssueTicketCommand" /> declares.
/// </summary>
/// <param name="Number">The issued ticket number.</param>
public sealed record TicketNumber(string Number)
{
    /// <summary>
    ///     Renders the ticket number alone, so an assertion on the recorded value stays readable.
    /// </summary>
    /// <returns>The ticket number.</returns>
    public override string ToString()
    {
        return Number;
    }
}

/// <summary>
///     A command declaring a reference-type result.
/// </summary>
internal sealed class IssueTicketCommand : ICommand<TicketNumber>;

/// <summary>
///     Handles <see cref="IssueTicketCommand" />.
/// </summary>
internal sealed class IssueTicketCommandHandler : ICommandHandler<IssueTicketCommand, TicketNumber>
{
    /// <inheritdoc />
    public Task<TicketNumber> HandleAsync(IssueTicketCommand message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TicketNumber("T-1"));
    }
}

/// <summary>
///     A command declaring a value-type result, so the generic handler is exercised over both kinds.
/// </summary>
internal sealed class VoidTicketCommand : ICommand<bool>;

/// <summary>
///     Handles <see cref="VoidTicketCommand" />.
/// </summary>
internal sealed class VoidTicketCommandHandler : ICommandHandler<VoidTicketCommand, bool>
{
    /// <inheritdoc />
    public Task<bool> HandleAsync(VoidTicketCommand message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}

/// <summary>
///     A command declaring no result, which a typed generic handler cannot be closed for.
/// </summary>
internal sealed class PurgeTicketsCommand : ICommand;

/// <summary>
///     Handles <see cref="PurgeTicketsCommand" />.
/// </summary>
internal sealed class PurgeTicketsCommandHandler : ICommandHandler<PurgeTicketsCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(PurgeTicketsCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A generic post-handler taking the message type and the result type its message declares.
/// </summary>
/// <typeparam name="TCommand">The command type, bound at registration.</typeparam>
/// <typeparam name="TCommandResult">The result type the command declares, bound at registration.</typeparam>
internal sealed class TypedResultPostHandler<TCommand, TCommandResult> : ICommandPostHandler<TCommand, TCommandResult>
    where TCommand : ICommand<TCommandResult>
    where TCommandResult : notnull
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly TypedResultRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TypedResultPostHandler{TCommand, TCommandResult}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public TypedResultPostHandler(TypedResultRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task PostHandleAsync(
        TCommand message,
        TCommandResult? messageResult,
        CancellationToken cancellationToken = default)
    {
        _recorder.Seen.Add($"{typeof(TCommandResult).Name}:{messageResult}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     An arity-2 handler whose second parameter is its own invention rather than a result type.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TContext">A parameter the registry has nothing to bind to.</typeparam>
internal sealed class ContextualPostHandler<TCommand, TContext> : ICommandPostHandler<TCommand>
    where TCommand : ICommand
{
    /// <inheritdoc />
    public Task PostHandleAsync(
        TCommand message,
        object? messageResult,
        CancellationToken cancellationToken = default)
    {
        _ = default(TContext);
        return Task.CompletedTask;
    }
}
