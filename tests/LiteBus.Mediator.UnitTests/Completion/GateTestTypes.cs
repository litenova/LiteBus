using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Completion;

/// <summary>
///     The decision the test asks the gate to take.
/// </summary>
internal enum GateDecision
{
    /// <summary>
    ///     Let the pipeline proceed.
    /// </summary>
    Continue = 0,

    /// <summary>
    ///     Stop because the result is already known.
    /// </summary>
    ShortCircuit = 1,

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
    ///     Gets or sets the decision the gate takes.
    /// </summary>
    public GateDecision Decision { get; set; }

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
    ///     Gets or sets a value indicating whether a second pre-handler ran after the gate stopped the pipeline.
    /// </summary>
    public bool SecondGateRan { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether an error handler saw the mediation.
    /// </summary>
    public bool ErrorHandlerRan { get; set; }
}

/// <summary>
///     Stops the pipeline the way the command asks.
/// </summary>
internal sealed class GatedCommandGate : ICommandGate<GatedCommand>
{
    /// <inheritdoc />
    public Task<PipelineDirective> DecideAsync(GatedCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(message.Decision switch
        {
            GateDecision.ShortCircuit => PipelineDirective.ShortCircuit("already applied"),
            GateDecision.Deny => PipelineDirective.Deny("the caller may not do this"),
            _ => PipelineDirective.Continue
        });
    }
}

/// <summary>
///     Runs at a later priority to prove pre-handlers after a stopping directive do not run.
/// </summary>
[HandlerPriority(10)]
internal sealed class NeverReachedGate : ICommandPreHandler<GatedCommand>
{
    /// <inheritdoc />
    public Task PreHandleAsync(GatedCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.SecondGateRan = true;
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
///     A command with a result, used to assert how a stopping directive supplies one.
/// </summary>
internal sealed class CachedValueCommand : ICommand<string>
{
    /// <summary>
    ///     Gets or sets the decision the gate takes.
    /// </summary>
    public GateDecision Decision { get; set; }
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
///     Answers from cache or refuses with a value, using the typed directive the compiler checks.
/// </summary>
internal sealed class CachedValueGate : ICommandGate<CachedValueCommand, string>
{
    /// <inheritdoc />
    public Task<PipelineDirective<string>> DecideAsync(
        CachedValueCommand message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(message.Decision switch
        {
            GateDecision.ShortCircuit => PipelineDirective<string>.ShortCircuit("from-cache", "served from cache"),
            GateDecision.Deny => PipelineDirective<string>.Deny("not your order", "refused"),
            _ => PipelineDirective<string>.Continue
        });
    }
}

/// <summary>
///     Refuses a command that produces a result without supplying one, so the refusal reaches the caller as an exception.
/// </summary>
internal sealed class UnansweredDenialGate : ICommandGate<CachedValueCommand, string>
{
    /// <inheritdoc />
    public Task<PipelineDirective<string>> DecideAsync(
        CachedValueCommand message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(message.Decision == GateDecision.Deny
            ? PipelineDirective<string>.Deny("nothing to hand back")
            : PipelineDirective<string>.Continue);
    }
}

/// <summary>
///     Stops a command that produces a result while written against the untyped contract, which cannot supply one.
/// </summary>
/// <remarks>
///     This is the mistake the typed gate exists to prevent, and the pipeline reports it as a configuration error rather
///     than handing the caller a default value.
/// </remarks>
internal sealed class ResultlessGate : ICommandGate<CachedValueCommand>
{
    /// <inheritdoc />
    public Task<PipelineDirective> DecideAsync(
        CachedValueCommand message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(message.Decision == GateDecision.ShortCircuit
            ? PipelineDirective.ShortCircuit("no result supplied")
            : PipelineDirective.Continue);
    }
}
