using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Counts what each stage did, so an evaluation can be shown to run the decision stages and nothing else.
/// </summary>
public sealed class StageActivityRecorder
{
    /// <summary>
    ///     Gets the stage names that ran, in order.
    /// </summary>
    public List<string> Ran { get; } = [];

    /// <summary>
    ///     Gets or sets a value indicating whether the guard denies.
    /// </summary>
    public bool Deny { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the validator reports the message malformed.
    /// </summary>
    public bool Invalid { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the shortcut answers.
    /// </summary>
    public bool Answer { get; set; }
}

/// <summary>
///     A command whose every stage is steered by the recorder.
/// </summary>
internal sealed class SteeredCommand : ICommand;

/// <summary>
///     A command with a result, so the typed result surface can be exercised.
/// </summary>
internal sealed class SteeredResultCommand : ICommand<string>;

/// <summary>
///     A query with a result, for the query surface.
/// </summary>
internal sealed class SteeredQuery : IQuery<string>;

/// <summary>
///     Denies on request and records that it ran.
/// </summary>
/// <typeparam name="TMessage">The steered command type.</typeparam>
/// <remarks>
///     Written against the messaging-level contract and constrained to the command axis, which is what makes one
///     implementation registrable per axis instead of copied per axis.
/// </remarks>
internal sealed class SteeredGuard<TMessage> : IMessageGuard<TMessage>
    where TMessage : ICommand
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly StageActivityRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SteeredGuard{TMessage}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public SteeredGuard(StageActivityRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<Verdict> DecideAsync(TMessage message, CancellationToken cancellationToken = default)
    {
        _recorder.Ran.Add("guard");

        return Task.FromResult(_recorder.Deny
            ? Verdict.Deny("not permitted", code: "NOT_PERMITTED")
            : Verdict.Allow);
    }
}

/// <summary>
///     Reports the message malformed on request and records that it ran.
/// </summary>
/// <typeparam name="TMessage">The steered command type.</typeparam>
internal sealed class SteeredValidator<TMessage> : IMessageValidator<TMessage>
    where TMessage : ICommand
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly StageActivityRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SteeredValidator{TMessage}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public SteeredValidator(StageActivityRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<Validity> ValidateAsync(TMessage message, CancellationToken cancellationToken = default)
    {
        _recorder.Ran.Add("validator");

        return Task.FromResult(_recorder.Invalid
            ? Validity.Invalid("the amount must be positive", "Amount", "AMOUNT_POSITIVE")
            : Validity.Valid);
    }
}

/// <summary>
///     Answers on request and records that it ran, standing in for a shortcut with side effects.
/// </summary>
/// <remarks>
///     The shipped idempotency shortcut claims a key when it runs, so an evaluation that ran shortcuts would burn keys
///     for messages nobody submitted. This records the fact of running for exactly that reason.
/// </remarks>
internal sealed class SteeredShortcut : ICommandShortcut<SteeredCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly StageActivityRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SteeredShortcut" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public SteeredShortcut(StageActivityRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<Shortcut> TryAnswerAsync(SteeredCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Ran.Add("shortcut");

        return Task.FromResult(_recorder.Answer
            ? Shortcut.Answer("the work was already applied", code: "ALREADY_APPLIED")
            : Shortcut.None);
    }
}

/// <summary>
///     Records that it ran, standing in for a pre-handler that does work.
/// </summary>
internal sealed class SteeredPreHandler : ICommandPreHandler<SteeredCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly StageActivityRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SteeredPreHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public SteeredPreHandler(StageActivityRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task PreHandleAsync(SteeredCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Ran.Add("pre-handler");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Records that the main handler ran.
/// </summary>
internal sealed class SteeredCommandHandler : ICommandHandler<SteeredCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly StageActivityRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SteeredCommandHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public SteeredCommandHandler(StageActivityRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(SteeredCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Ran.Add("main");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Produces a value for the typed result surface.
/// </summary>
internal sealed class SteeredResultCommandHandler : ICommandHandler<SteeredResultCommand, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(SteeredResultCommand message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("handled");
    }
}

/// <summary>
///     Produces a value for the query surface.
/// </summary>
internal sealed class SteeredQueryHandler : IQueryHandler<SteeredQuery, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(SteeredQuery message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("queried");
    }
}

/// <summary>
///     Maps a refusal onto the caller's own result shape, so a refusal arrives as a value.
/// </summary>
internal sealed class SteeredRefusalMapper : ICommandRefusalMapper<SteeredResultCommand, string>
{
    /// <inheritdoc />
    public string Map(SteeredResultCommand message, Refusal refusal)
    {
        return $"refused:{refusal.Code}";
    }
}

/// <summary>
///     Throws after the main handler, standing in for a genuine fault the Try methods must not swallow.
/// </summary>
internal sealed class ThrowingSteeredPostHandler : ICommandPostHandler<SteeredCommand>
{
    /// <inheritdoc />
    public Task PostHandleAsync(
        SteeredCommand message,
        object? messageResult,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("the projection store is unreachable");
    }
}

/// <summary>
///     The query-axis counterpart of <see cref="SteeredGuard{TMessage}" />.
/// </summary>
/// <typeparam name="TMessage">The steered query type.</typeparam>
internal sealed class SteeredQueryGuard<TMessage> : IMessageGuard<TMessage>
    where TMessage : IQuery
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly StageActivityRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SteeredQueryGuard{TMessage}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public SteeredQueryGuard(StageActivityRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<Verdict> DecideAsync(TMessage message, CancellationToken cancellationToken = default)
    {
        _recorder.Ran.Add("guard");

        return Task.FromResult(_recorder.Deny
            ? Verdict.Deny("not permitted", code: "NOT_PERMITTED")
            : Verdict.Allow);
    }
}

/// <summary>
///     The query-axis counterpart of <see cref="SteeredValidator{TMessage}" />.
/// </summary>
/// <typeparam name="TMessage">The steered query type.</typeparam>
internal sealed class SteeredQueryValidator<TMessage> : IMessageValidator<TMessage>
    where TMessage : IQuery
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly StageActivityRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SteeredQueryValidator{TMessage}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public SteeredQueryValidator(StageActivityRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<Validity> ValidateAsync(TMessage message, CancellationToken cancellationToken = default)
    {
        _recorder.Ran.Add("validator");

        return Task.FromResult(_recorder.Invalid
            ? Validity.Invalid("the amount must be positive", "Amount", "AMOUNT_POSITIVE")
            : Validity.Valid);
    }
}

/// <summary>
///     A command nothing is registered for, so the explainer can be asked about a message it has never seen.
/// </summary>
internal sealed class UnregisteredCommand : ICommand;
