using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Completion;

/// <summary>
///     The decision the test asks the guard and shortcut to take.
/// </summary>
internal enum StageDecision
{
    /// <summary>
    ///     Let the mediation proceed.
    /// </summary>
    Proceed = 0,

    /// <summary>
    ///     Answer because the work is already done.
    /// </summary>
    Answer = 1,

    /// <summary>
    ///     Refuse the message.
    /// </summary>
    Deny = 2
}

/// <summary>
///     A command whose pipeline is steered by the test.
/// </summary>
internal sealed class GatedCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the decision the guard and shortcut take.
    /// </summary>
    public StageDecision Decision { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the handler suppresses post-handlers.
    /// </summary>
    public bool ShouldSuppressPostHandlers { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the post-handler ran.
    /// </summary>
    public bool PostHandlerRan { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether a pre-handler ran after a stage stopped the pipeline.
    /// </summary>
    public bool LatePreHandlerRan { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether an error handler saw the mediation.
    /// </summary>
    public bool ErrorHandlerRan { get; set; }
}

/// <summary>
///     Refuses the command when the test asks for a denial.
/// </summary>
internal sealed class GatedCommandGuard : ICommandGuard<GatedCommand>
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(GatedCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(message.Decision == StageDecision.Deny
            ? Verdict.Deny("the caller may not do this")
            : Verdict.Allow);
    }
}

/// <summary>
///     Skips the command when the test asks for an answer.
/// </summary>
internal sealed class GatedCommandShortcut : ICommandShortcut<GatedCommand>
{
    /// <inheritdoc />
    public Task<Shortcut> TryAnswerAsync(GatedCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(message.Decision == StageDecision.Answer
            ? Shortcut.Skip("already applied")
            : Shortcut.None);
    }
}

/// <summary>
///     Runs at a later priority to prove pre-handlers after a stopping decision do not run.
/// </summary>
[HandlerPriority(10)]
internal sealed class NeverReachedPreHandler : ICommandPreHandler<GatedCommand>
{
    /// <inheritdoc />
    public Task PreHandleAsync(GatedCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.LatePreHandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     Records that it ran, so a denial reaching error handlers would be visible.
/// </summary>
internal sealed class GatedCommandErrorHandler : ICommandErrorHandler<GatedCommand>
{
    /// <inheritdoc />
    public Task HandleErrorAsync(
        MessageErrorContext<GatedCommand, object> context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Message.ErrorHandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     Records that it ran, and suppresses post-handlers when the command asks for it.
/// </summary>
internal sealed class GatedCommandHandler : ICommandHandler<GatedCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(GatedCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;

        if (message.ShouldSuppressPostHandlers)
        {
            AmbientExecutionContext.Current.SuppressPostHandlers();
        }

        return Task.CompletedTask;
    }
}

/// <summary>
///     Records that it ran, so suppression can be observed.
/// </summary>
internal sealed class GatedCommandPostHandler : ICommandPostHandler<GatedCommand>
{
    /// <inheritdoc />
    public Task PostHandleAsync(GatedCommand message, object? messageResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.PostHandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     Records the completion context for the gated command.
/// </summary>
internal sealed class DirectCompletionHandlerForGated : ICommandCompletionHandler<GatedCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CompletionRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DirectCompletionHandlerForGated" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public DirectCompletionHandlerForGated(CompletionRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<GatedCommand> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.Observed.Enqueue(("gated", context.AsUntyped()));
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command with a result, used to assert how a stopping decision supplies one.
/// </summary>
internal sealed class CachedValueCommand : ICommand<string>
{
    /// <summary>
    ///     Gets or sets the decision the guard and shortcut take.
    /// </summary>
    public StageDecision Decision { get; set; }
}

/// <summary>
///     Returns a value the test can distinguish from the cached one.
/// </summary>
internal sealed class CachedValueCommandHandler : ICommandHandler<CachedValueCommand, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(CachedValueCommand message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("from-handler");
    }
}

/// <summary>
///     Answers from cache, using the typed shortcut the compiler checks.
/// </summary>
internal sealed class CachedValueShortcut : ICommandShortcut<CachedValueCommand, string>
{
    /// <inheritdoc />
    public Task<Shortcut<string>> TryAnswerAsync(
        CachedValueCommand message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(message.Decision == StageDecision.Answer
            ? Shortcut<string>.Answer("from-cache", "served from cache")
            : Shortcut<string>.None);
    }
}

/// <summary>
///     Refuses with a code, so a registered refusal mapper can turn the decision into a value for the caller.
/// </summary>
internal sealed class CodedRefusalGuard : ICommandGuard<CachedValueCommand>
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(CachedValueCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(message.Decision == StageDecision.Deny
            ? Verdict.Deny("not your order", "NOT_OWNER")
            : Verdict.Allow);
    }
}

/// <summary>
///     Maps a refused <see cref="CachedValueCommand" /> to the string the caller receives.
/// </summary>
/// <remarks>
///     Registered against the concrete command, which is the shape that must win over a mapper registered for a base
///     type.
/// </remarks>
internal sealed class CachedValueRefusalMapper : ICommandRefusalMapper<CachedValueCommand, string>
{
    /// <inheritdoc />
    public string Map(CachedValueCommand message, Refusal refusal)
    {
        return $"refused:{refusal.Code ?? refusal.Outcome.ToString()}";
    }
}

/// <summary>
///     Refuses a command that produces a result through the untyped guard, which is correct and raises the denial.
/// </summary>
internal sealed class UntypedGuardOnResultCommand : ICommandGuard<CachedValueCommand>
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(CachedValueCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(message.Decision == StageDecision.Deny
            ? Verdict.Deny("refused by the untyped guard")
            : Verdict.Allow);
    }
}

/// <summary>
///     Answers a command that produces a result while written against the untyped contract, which cannot supply one.
/// </summary>
/// <remarks>
///     This is the mistake the typed shortcut exists to prevent and analyzer rule LB1019 reports. The pipeline treats it
///     as a configuration error rather than handing the caller a default value.
/// </remarks>
internal sealed class ResultlessShortcut : ICommandShortcut<CachedValueCommand>
{
    /// <inheritdoc />
    public Task<Shortcut> TryAnswerAsync(
        CachedValueCommand message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(message.Decision == StageDecision.Answer
            ? Shortcut.Skip("no result supplied")
            : Shortcut.None);
    }
}

/// <summary>
///     Records the order in which the decision stages ran.
/// </summary>
internal sealed class StageOrderRecorder
{
    /// <summary>
    ///     Gets the stage names in the order they ran.
    /// </summary>
    public List<string> Observed { get; } = [];
}

/// <summary>
///     A command used to assert that guards run before shortcuts regardless of registration scope.
/// </summary>
internal sealed class OrderedCommand : ICommand;

/// <summary>
///     A shortcut registered for every command, so it is indirect and would run first under one merged stage.
/// </summary>
/// <remarks>
///     This is the shape that made the old single-stage pipeline unsafe. An indirect handler runs before every direct
///     one, so a globally registered cache could answer before a message-specific authorization check existed to object.
/// </remarks>
internal sealed class IndirectAnsweringShortcut : ICommandShortcut<ICommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly StageOrderRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="IndirectAnsweringShortcut" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public IndirectAnsweringShortcut(StageOrderRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<Shortcut> TryAnswerAsync(ICommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Observed.Add("shortcut");
        return Task.FromResult(Shortcut.Skip("answered by the global cache"));
    }
}

/// <summary>
///     A guard registered for one command, so it is direct and carries the later priority.
/// </summary>
/// <remarks>
///     Both properties would have put this behind <see cref="IndirectAnsweringShortcut" /> in a single stage. The fixed
///     stage order runs it first anyway, which is the guarantee the test asserts.
/// </remarks>
[HandlerPriority(100)]
internal sealed class DirectRefusingGuard : ICommandGuard<OrderedCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly StageOrderRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DirectRefusingGuard" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public DirectRefusingGuard(StageOrderRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<Verdict> DecideAsync(OrderedCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Observed.Add("guard");
        return Task.FromResult(Verdict.Deny("the caller is not permitted"));
    }
}

/// <summary>
///     Records that it ran, so a guard that failed to stop the pipeline would be visible.
/// </summary>
internal sealed class OrderedCommandHandler : ICommandHandler<OrderedCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly StageOrderRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OrderedCommandHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public OrderedCommandHandler(StageOrderRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(OrderedCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Observed.Add("handler");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Records that the pre-handler stage ran after both decision stages allowed the message.
/// </summary>
/// <summary>
///     A validator that accepts every ordered command, used to observe the stage order on the success path.
/// </summary>
/// <remarks>
///     Carries a priority ahead of the guard's, which under priority ordering alone would run it first. The stage order
///     has to beat that.
/// </remarks>
[HandlerPriority(1)]
internal sealed class AllowingOrderedValidator : ICommandValidator<OrderedCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly StageOrderRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AllowingOrderedValidator" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public AllowingOrderedValidator(StageOrderRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<Validity> ValidateAsync(OrderedCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Observed.Add("validator");

        return Task.FromResult(Validity.Valid);
    }
}

internal sealed class OrderedCommandPreHandler : ICommandPreHandler<OrderedCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly StageOrderRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OrderedCommandPreHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public OrderedCommandPreHandler(StageOrderRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task PreHandleAsync(OrderedCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Observed.Add("pre-handler");
        return Task.CompletedTask;
    }
}

/// <summary>
///     A guard that allows every ordered command, used to observe the stage order on the success path.
/// </summary>
[HandlerPriority(100)]
internal sealed class AllowingOrderedGuard : ICommandGuard<OrderedCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly StageOrderRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AllowingOrderedGuard" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public AllowingOrderedGuard(StageOrderRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<Verdict> DecideAsync(OrderedCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Observed.Add("guard");
        return Task.FromResult(Verdict.Allow);
    }
}

/// <summary>
///     A shortcut that answers nothing, used to observe the stage order on the success path.
/// </summary>
internal sealed class PassiveOrderedShortcut : ICommandShortcut<ICommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly StageOrderRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PassiveOrderedShortcut" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public PassiveOrderedShortcut(StageOrderRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<Shortcut> TryAnswerAsync(ICommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Observed.Add("shortcut");
        return Task.FromResult(Shortcut.None);
    }
}
