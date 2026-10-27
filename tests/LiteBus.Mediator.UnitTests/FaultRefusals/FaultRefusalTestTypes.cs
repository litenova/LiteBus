using System.Collections.Concurrent;
using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.FaultRefusals;

/// <summary>
///     Collects the stage calls observed during one fault refusal test.
/// </summary>
internal sealed class FaultRefusalRecorder
{
    /// <summary>
    ///     Gets the calls observed, in the order they happened.
    /// </summary>
    public ConcurrentQueue<string> Calls { get; } = new();
}

/// <summary>
///     The fault the test classifiers recognize as a denial.
/// </summary>
internal sealed class ConflictException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ConflictException" /> class.
    /// </summary>
    /// <param name="message">The message describing the conflict.</param>
    public ConflictException(string message)
        : base(message)
    {
    }
}

/// <summary>
///     The fault the test classifier recognizes as invalid input.
/// </summary>
internal sealed class InvalidFaultException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="InvalidFaultException" /> class.
    /// </summary>
    /// <param name="message">The message describing the fault.</param>
    public InvalidFaultException(string message)
        : base(message)
    {
    }
}

/// <summary>
///     A command whose stages throw the fault the test asks for.
/// </summary>
internal sealed class ConflictCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler throws the recognized conflict.
    /// </summary>
    public bool ThrowConflict { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the main handler throws a fault no classifier recognizes.
    /// </summary>
    public bool ThrowUnrecognized { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the main handler throws the invalid-shaped fault.
    /// </summary>
    public bool ThrowInvalid { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the main handler observes cancellation.
    /// </summary>
    public bool ShouldCancel { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the guard throws the recognized conflict.
    /// </summary>
    public bool GuardThrows { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the post-handler throws the recognized conflict.
    /// </summary>
    public bool PostThrows { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the commit completion handler throws the recognized conflict.
    /// </summary>
    public bool CommitThrows { get; set; }
}

/// <summary>
///     Throws the fault the command under test asks for.
/// </summary>
internal sealed class ConflictCommandHandler : ICommandHandler<ConflictCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly FaultRefusalRecorder _calls;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ConflictCommandHandler" /> class.
    /// </summary>
    /// <param name="calls">The recorder shared with the test.</param>
    public ConflictCommandHandler(FaultRefusalRecorder calls)
    {
        _calls = calls;
    }

    /// <inheritdoc />
    public Task HandleAsync(ConflictCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _calls.Calls.Enqueue("main");

        if (message.ShouldCancel)
        {
            throw new OperationCanceledException();
        }

        if (message.ThrowInvalid)
        {
            throw new InvalidFaultException("the operation is not acceptable");
        }

        if (message.ThrowUnrecognized)
        {
            throw new InvalidOperationException("boom");
        }

        if (message.ThrowConflict)
        {
            throw new ConflictException("the reference is already taken");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
///     A result-bearing command whose handler throws the recognized conflict on request.
/// </summary>
internal sealed class ConflictResultCommand : ICommand<string>
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler throws the recognized conflict.
    /// </summary>
    public bool ThrowConflict { get; set; }
}

/// <summary>
///     Throws the recognized conflict when the command asks for it, and otherwise produces a value.
/// </summary>
internal sealed class ConflictResultCommandHandler : ICommandHandler<ConflictResultCommand, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(ConflictResultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.ThrowConflict)
        {
            throw new ConflictException("the reference is already taken");
        }

        return Task.FromResult("handled");
    }
}

/// <summary>
///     Throws the recognized conflict when the command asks for it, standing in for a guard that fails closed.
/// </summary>
internal sealed class ConflictCommandGuard : ICommandGuard<ConflictCommand>
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(ConflictCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.GuardThrows)
        {
            throw new ConflictException("the guard hit a conflict");
        }

        return Task.FromResult(Verdict.Allow);
    }
}

/// <summary>
///     Throws the recognized conflict when the command asks for it, after recording that it ran.
/// </summary>
internal sealed class ConflictCommandPostHandler : ICommandPostHandler<ConflictCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly FaultRefusalRecorder _calls;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ConflictCommandPostHandler" /> class.
    /// </summary>
    /// <param name="calls">The recorder shared with the test.</param>
    public ConflictCommandPostHandler(FaultRefusalRecorder calls)
    {
        _calls = calls;
    }

    /// <inheritdoc />
    public Task PostHandleAsync(ConflictCommand message, object? messageResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _calls.Calls.Enqueue("post");

        if (message.PostThrows)
        {
            throw new ConflictException("the post-handler hit a conflict");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
///     Records that it ran and leaves the fault unhandled, so an unclassified fault still propagates.
/// </summary>
internal sealed class ConflictErrorHandler : ICommandErrorHandler<ConflictCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly FaultRefusalRecorder _calls;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ConflictErrorHandler" /> class.
    /// </summary>
    /// <param name="calls">The recorder shared with the test.</param>
    public ConflictErrorHandler(FaultRefusalRecorder calls)
    {
        _calls = calls;
    }

    /// <inheritdoc />
    public Task HandleErrorAsync(MessageErrorContext<ConflictCommand, object> context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        _calls.Calls.Enqueue("error");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Records the completion context so the test can assert which outcome a classified fault reported.
/// </summary>
internal sealed class ConflictCompletionHandler : ICommandCompletionHandler<ConflictCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CompletionRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ConflictCompletionHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public ConflictCompletionHandler(CompletionRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<ConflictCommand> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.Observed.Enqueue(("conflict", context.AsUntyped()));
        return Task.CompletedTask;
    }
}

/// <summary>
///     Throws the recognized conflict from the unit-of-work priority, standing in for a commit that reports a
///     duplicate key.
/// </summary>
[HandlerPriority(HandlerPriorities.UnitOfWork)]
internal sealed class ConflictCommitHandler : ICommandCompletionHandler<ConflictCommand>
{
    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<ConflictCommand> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Message.CommitThrows)
        {
            throw new ConflictException("the commit hit a conflict");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
///     Maps a refusal onto the caller's result shape and records the code it was given.
/// </summary>
internal sealed class ConflictResultMapper : ICommandRefusalMapper<ConflictResultCommand, string>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly FaultRefusalRecorder _calls;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ConflictResultMapper" /> class.
    /// </summary>
    /// <param name="calls">The recorder shared with the test.</param>
    public ConflictResultMapper(FaultRefusalRecorder calls)
    {
        _calls = calls;
    }

    /// <inheritdoc />
    public string Map(ConflictResultCommand message, Refusal refusal)
    {
        _calls.Calls.Enqueue($"mapper:{refusal.Code}");
        return $"refused:{refusal.Code}";
    }
}

/// <summary>
///     Recognizes the test conflict as a denial.
/// </summary>
internal sealed class ConflictClassifier : IFaultRefusalClassifier
{
    /// <inheritdoc />
    public bool TryClassify(Exception exception, out Refusal refusal)
    {
        if (exception is ConflictException)
        {
            refusal = Refusal.Denied("the reference is already taken", "CONFLICT");
            return true;
        }

        refusal = default;
        return false;
    }
}

/// <summary>
///     Recognizes the invalid-shaped test fault as invalid input.
/// </summary>
internal sealed class InvalidFaultClassifier : IFaultRefusalClassifier
{
    /// <inheritdoc />
    public bool TryClassify(Exception exception, out Refusal refusal)
    {
        if (exception is InvalidFaultException)
        {
            refusal = Refusal.Invalid("the operation is not acceptable", "NOT_ACCEPTABLE");
            return true;
        }

        refusal = default;
        return false;
    }
}

/// <summary>
///     Recognizes the test conflict with a caller-supplied code, so order tests can tell two matches apart.
/// </summary>
internal sealed class FixedCodeClassifier : IFaultRefusalClassifier
{
    /// <summary>
    ///     The code reported for a recognized conflict.
    /// </summary>
    private readonly string _code;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FixedCodeClassifier" /> class.
    /// </summary>
    /// <param name="code">The code reported for a recognized conflict.</param>
    public FixedCodeClassifier(string code)
    {
        _code = code;
    }

    /// <inheritdoc />
    public bool TryClassify(Exception exception, out Refusal refusal)
    {
        if (exception is ConflictException)
        {
            refusal = Refusal.Denied($"classified by {_code}", _code);
            return true;
        }

        refusal = default;
        return false;
    }
}

/// <summary>
///     Records every fault it is offered and recognizes all of them, proving cancellation is never offered.
/// </summary>
internal sealed class RecordingCatchAllClassifier : IFaultRefusalClassifier
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly FaultRefusalRecorder _calls;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecordingCatchAllClassifier" /> class.
    /// </summary>
    /// <param name="calls">The recorder shared with the test.</param>
    public RecordingCatchAllClassifier(FaultRefusalRecorder calls)
    {
        _calls = calls;
    }

    /// <inheritdoc />
    public bool TryClassify(Exception exception, out Refusal refusal)
    {
        _calls.Calls.Enqueue($"offered:{exception.GetType().Name}");
        refusal = Refusal.Denied("caught everything", "CAUGHT");
        return true;
    }
}

/// <summary>
///     A query whose handler always throws the recognized conflict.
/// </summary>
internal sealed class ConflictQuery : IQuery<string>
{
}

/// <summary>
///     Throws the recognized conflict for every query.
/// </summary>
internal sealed class ConflictQueryHandler : IQueryHandler<ConflictQuery, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(ConflictQuery message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        throw new ConflictException("the query hit a conflict");
    }
}

/// <summary>
///     An event whose subscriber always throws the recognized conflict.
/// </summary>
internal sealed class ConflictEvent : IEvent
{
}

/// <summary>
///     Throws the recognized conflict for every publish.
/// </summary>
internal sealed class ConflictEventHandler : IEventHandler<ConflictEvent>
{
    /// <inheritdoc />
    public Task HandleAsync(ConflictEvent message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        throw new ConflictException("the subscriber hit a conflict");
    }
}

/// <summary>
///     Records the completion context so the test can assert which outcome a classified publish reported.
/// </summary>
internal sealed class ConflictEventCompletionHandler : IEventCompletionHandler<ConflictEvent>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CompletionRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ConflictEventCompletionHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public ConflictEventCompletionHandler(CompletionRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<ConflictEvent> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.Observed.Enqueue(("conflict-event", context.AsUntyped()));
        return Task.CompletedTask;
    }
}

/// <summary>
///     An audited command whose handler throws the recognized conflict on request.
/// </summary>
[Audited("test.conflict-fault")]
internal sealed class AuditedConflictCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler throws the recognized conflict.
    /// </summary>
    public bool ThrowConflict { get; set; }
}

/// <summary>
///     Throws the recognized conflict when the audited command asks for it.
/// </summary>
internal sealed class AuditedConflictCommandHandler : ICommandHandler<AuditedConflictCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(AuditedConflictCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.ThrowConflict)
        {
            throw new ConflictException("the reference is already taken");
        }

        return Task.CompletedTask;
    }
}
