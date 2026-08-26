using System.Collections.Concurrent;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Completion;

/// <summary>
///     Collects the completion contexts observed during one test run.
/// </summary>
internal sealed class CompletionRecorder
{
    /// <summary>
    ///     Gets the completion contexts observed, in the order the handlers ran.
    /// </summary>
    public ConcurrentQueue<(string Handler, MessageCompletionContext Context)> Observed { get; } = new();
}

/// <summary>
///     A command whose handler behavior is chosen by the test.
/// </summary>
internal sealed class CompletionCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler throws.
    /// </summary>
    public bool ShouldThrow { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the pre-handler aborts.
    /// </summary>
    public bool ShouldAbort { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the main handler cancels.
    /// </summary>
    public bool ShouldCancel { get; set; }
}

/// <summary>
///     A command with a result used to assert that completion observes the produced result.
/// </summary>
internal sealed class CompletionCommandWithResult : ICommand<string>;

/// <summary>
///     Short-circuits the pipeline with a reason when the command asks for it.
/// </summary>
internal sealed class CompletionCommandPreHandler : ICommandShortCircuitingPreHandler<CompletionCommand>
{
    /// <inheritdoc />
    public Task<PipelineDirective> PreHandleAsync(CompletionCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.ShouldAbort
            ? Task.FromResult(PipelineDirective.ShortCircuit(reason: "not permitted"))
            : Task.FromResult(PipelineDirective.Continue);
    }
}

/// <summary>
///     Produces the outcome requested by the command under test.
/// </summary>
internal sealed class CompletionCommandHandler : ICommandHandler<CompletionCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(CompletionCommand message, CancellationToken cancellationToken = default)
    {
        if (message.ShouldCancel)
        {
            throw new OperationCanceledException();
        }

        if (message.ShouldThrow)
        {
            throw new InvalidOperationException("handler failed");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
///     Returns a fixed result so completion can assert on it.
/// </summary>
internal sealed class CompletionCommandWithResultHandler : ICommandHandler<CompletionCommandWithResult, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(CompletionCommandWithResult message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("produced");
    }
}

/// <summary>
///     Swallows the failure so the pipeline does not rethrow during outcome tests.
/// </summary>
internal sealed class CompletionCommandErrorHandler : ICommandErrorHandler<CompletionCommand>
{
    /// <inheritdoc />
    public Task HandleErrorAsync(MessageErrorContext<CompletionCommand, object> context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Outcome = MessageErrorOutcome.Handled;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A completion handler registered for the concrete command type.
/// </summary>
internal sealed class DirectCompletionHandler : ICommandCompletionHandler<CompletionCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CompletionRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DirectCompletionHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public DirectCompletionHandler(CompletionRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<CompletionCommand> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.Observed.Enqueue(("direct", context.AsUntyped()));
        return Task.CompletedTask;
    }
}

/// <summary>
///     A completion handler registered for every command.
/// </summary>
internal sealed class GlobalCompletionHandler : ICommandCompletionHandler
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CompletionRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="GlobalCompletionHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public GlobalCompletionHandler(CompletionRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<ICommand> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.Observed.Enqueue(("global", context.AsUntyped()));
        return Task.CompletedTask;
    }
}

/// <summary>
///     A completion handler that always throws, used to assert the suppression policy.
/// </summary>
internal sealed class ThrowingCompletionHandler : ICommandCompletionHandler<CompletionCommand>
{
    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<CompletionCommand> context, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("observer failed");
    }
}
