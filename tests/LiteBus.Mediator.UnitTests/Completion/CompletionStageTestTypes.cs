using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Completion;

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
    ///     Gets or sets a value indicating whether the guard refuses the command.
    /// </summary>
    public bool ShouldDeny { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the main handler cancels.
    /// </summary>
    public bool ShouldCancel { get; set; }
}

/// <summary>
///     A command with a result used to assert that completion observes the produced result.
/// </summary>
internal sealed class CompletionCommandWithResult : ICommand<string>
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler throws, so an error handler recovers the result.
    /// </summary>
    public bool ShouldThrow { get; set; }
}

/// <summary>
///     Refuses the command with a reason when the command asks for it.
/// </summary>
internal sealed class CompletionCommandGuard : ICommandGuard<CompletionCommand>
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(CompletionCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.ShouldDeny
            ? Task.FromResult(Verdict.Deny("not permitted"))
            : Task.FromResult(Verdict.Allow);
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
        ArgumentNullException.ThrowIfNull(message);

        return message.ShouldThrow
            ? throw new InvalidOperationException("handler failed")
            : Task.FromResult("produced");
    }
}

/// <summary>
///     Recovers the failure with a replacement result, so the caller receives a value the main handler never produced.
/// </summary>
internal sealed class CompletionCommandWithResultErrorHandler : ICommandErrorHandler<CompletionCommandWithResult, string>
{
    /// <inheritdoc />
    public Task HandleErrorAsync(
        MessageErrorContext<CompletionCommandWithResult, string> context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.HandledResult = "recovered";
        context.Outcome = MessageErrorOutcome.Handled;
        return Task.CompletedTask;
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

/// <summary>
///     A completion handler registered for the command that produces a result, receiving the result typed.
/// </summary>
internal sealed class TypedResultCompletionHandler : ICommandCompletionHandler<CompletionCommandWithResult, string>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CompletionRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TypedResultCompletionHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public TypedResultCompletionHandler(CompletionRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(
        MessageCompletionContext<CompletionCommandWithResult, string> context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The result arrives as a string rather than as object, which is the point of the typed contract.
        _recorder.TypedResults.Enqueue((context.HasResult, context.MessageResult));
        _recorder.Observed.Enqueue(("typed", context.AsUntyped()));
        return Task.CompletedTask;
    }
}
