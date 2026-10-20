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
///     A generic shortcut taking the message type and the result type its message declares, standing in for a cache
///     that answers any command whose result it holds.
/// </summary>
/// <typeparam name="TCommand">The command type, bound at registration.</typeparam>
/// <typeparam name="TCommandResult">The result type the command declares, bound at registration.</typeparam>
/// <remarks>
///     A generic caching shortcut is only expressible through the typed contract, because the untyped one carries no
///     result. Registering it once has to cover every command that declares a result, which is what this type proves.
/// </remarks>
internal sealed class TypedResultShortcut<TCommand, TCommandResult> : ICommandShortcut<TCommand, TCommandResult>
    where TCommand : ICommand<TCommandResult>
    where TCommandResult : notnull
{
    /// <summary>
    ///     The answers the test seeded, keyed by command type.
    /// </summary>
    private readonly TypedResultCache _cache;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TypedResultShortcut{TCommand, TCommandResult}" /> class.
    /// </summary>
    /// <param name="cache">The answers the test seeded.</param>
    public TypedResultShortcut(TypedResultCache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc />
    public Task<Shortcut<TCommandResult>> TryAnswerAsync(
        TCommand message,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_cache.TryGet<TCommandResult>(typeof(TCommand), out var cached)
            ? Shortcut<TCommandResult>.Answer(cached, "the answer was cached", code: "CACHE_HIT")
            : Shortcut<TCommandResult>.None);
    }
}

/// <summary>
///     Holds the answers a generic shortcut serves, seeded per command type by the test.
/// </summary>
public sealed class TypedResultCache
{
    /// <summary>
    ///     The seeded answers, keyed by the command type they answer for.
    /// </summary>
    private readonly Dictionary<Type, object> _answers = [];

    /// <summary>
    ///     Seeds the answer served for one command type.
    /// </summary>
    /// <typeparam name="TCommandResult">The result type the command declares.</typeparam>
    /// <param name="commandType">The command type to answer for.</param>
    /// <param name="answer">The cached answer.</param>
    public void Seed<TCommandResult>(Type commandType, TCommandResult answer)
        where TCommandResult : notnull
    {
        _answers[commandType] = answer;
    }

    /// <summary>
    ///     Reads the answer seeded for one command type.
    /// </summary>
    /// <typeparam name="TCommandResult">The result type the command declares.</typeparam>
    /// <param name="commandType">The command type being answered.</param>
    /// <param name="answer">When this method returns <see langword="true" />, the cached answer.</param>
    /// <returns><see langword="true" /> when an answer was seeded for that command type.</returns>
    public bool TryGet<TCommandResult>(Type commandType, out TCommandResult answer)
    {
        if (_answers.TryGetValue(commandType, out var stored))
        {
            answer = (TCommandResult) stored;
            return true;
        }

        answer = default!;
        return false;
    }
}

/// <summary>
///     A generic refusal mapper taking the message type and the result type its message declares, standing in for the
///     one policy that decides what every refused caller receives.
/// </summary>
/// <typeparam name="TCommand">The command type, bound at registration.</typeparam>
/// <typeparam name="TCommandResult">The result type the command declares, bound at registration.</typeparam>
/// <remarks>
///     A mapper registered against <c>ICommand</c> covers one result type. Only a generic one covers every result
///     type at once, which is what "map every denial onto this shape" needs.
/// </remarks>
internal sealed class TypedResultRefusalMapper<TCommand, TCommandResult>
    : ICommandRefusalMapper<TCommand, TCommandResult>
    where TCommand : ICommand<TCommandResult>
    where TCommandResult : notnull
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly RefusalRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TypedResultRefusalMapper{TCommand, TCommandResult}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public TypedResultRefusalMapper(RefusalRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public TCommandResult Map(TCommand message, Refusal refusal)
    {
        _recorder.Seen.Add($"{typeof(TCommand).Name}:{refusal.Outcome}:{refusal.Code}");

        // Both parameters are bound, so the mapper produces the caller's own shape rather than an object the pipeline
        // has to cast.
        return typeof(TCommandResult) == typeof(TicketNumber)
            ? (TCommandResult) (object) new TicketNumber("refused")
            : default!;
    }
}

/// <summary>
///     Records what the generic refusal mapper saw.
/// </summary>
public sealed class RefusalRecorder
{
    /// <summary>
    ///     Gets the refusals the generic mapper mapped, in order.
    /// </summary>
    public List<string> Seen { get; } = [];
}

/// <summary>
///     A command that a guard always denies, used to reach the generic refusal mapper.
/// </summary>
internal sealed class DeniedTicketCommand : ICommand<TicketNumber>;

/// <summary>
///     Handles <see cref="DeniedTicketCommand" />, which the guard never lets run.
/// </summary>
internal sealed class DeniedTicketCommandHandler : ICommandHandler<DeniedTicketCommand, TicketNumber>
{
    /// <inheritdoc />
    public Task<TicketNumber> HandleAsync(DeniedTicketCommand message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TicketNumber("never"));
    }
}

/// <summary>
///     Denies <see cref="DeniedTicketCommand" /> so the refusal reaches the mapper.
/// </summary>
internal sealed class DeniedTicketCommandGuard : ICommandGuard<DeniedTicketCommand>
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(DeniedTicketCommand message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Verdict.Deny("not permitted", code: "NOT_PERMITTED"));
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
