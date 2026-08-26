using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Completion;

/// <summary>
///     A command whose pipeline is steered by the test.
/// </summary>
internal sealed class GatedCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the gate short-circuits the pipeline.
    /// </summary>
    public bool ShouldShortCircuit { get; set; }

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
    ///     Gets or sets a value indicating whether a second gate ran after the first short-circuited.
    /// </summary>
    public bool SecondGateRan { get; set; }
}

/// <summary>
///     Short-circuits the pipeline when the command asks for it.
/// </summary>
internal sealed class GatedCommandGate : ICommandShortCircuitingPreHandler<GatedCommand>
{
    /// <inheritdoc />
    public Task<PipelineDirective> PreHandleAsync(GatedCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.ShouldShortCircuit
            ? Task.FromResult(PipelineDirective.ShortCircuit(reason: "gate closed"))
            : Task.FromResult(PipelineDirective.Continue);
    }
}

/// <summary>
///     Runs at a later priority to prove pre-handlers after a short-circuit do not run.
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
///     A command with a result, used to assert short-circuit result handling.
/// </summary>
internal sealed class CachedValueCommand : ICommand<string>
{
    /// <summary>
    ///     Gets or sets a value indicating whether the gate short-circuits the pipeline.
    /// </summary>
    public bool ShouldShortCircuit { get; set; }
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
///     Short-circuits with a result, the way a cache would.
/// </summary>
internal sealed class CachedValueGate : ICommandShortCircuitingPreHandler<CachedValueCommand>
{
    /// <inheritdoc />
    public Task<PipelineDirective> PreHandleAsync(CachedValueCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.ShouldShortCircuit
            ? Task.FromResult(PipelineDirective.ShortCircuit("from-cache", "served from cache"))
            : Task.FromResult(PipelineDirective.Continue);
    }
}

/// <summary>
///     Short-circuits a result-returning command without supplying a result, which is a configuration error.
/// </summary>
internal sealed class ResultlessGate : ICommandShortCircuitingPreHandler<CachedValueCommand>
{
    /// <inheritdoc />
    public Task<PipelineDirective> PreHandleAsync(CachedValueCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.ShouldShortCircuit
            ? Task.FromResult(PipelineDirective.ShortCircuit(reason: "no result supplied"))
            : Task.FromResult(PipelineDirective.Continue);
    }
}
