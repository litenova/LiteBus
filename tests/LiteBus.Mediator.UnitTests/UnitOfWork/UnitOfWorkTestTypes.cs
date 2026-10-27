using System.Collections.Concurrent;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.UnitOfWork;

/// <summary>
///     Collects the commits and completion probes observed during one unit-of-work test run.
/// </summary>
internal sealed class UnitOfWorkRecorder
{
    /// <summary>
    ///     Gets the observed events, in the order they happened.
    /// </summary>
    public ConcurrentQueue<string> Events { get; } = new();
}

/// <summary>
///     A unit of work that records each commit instead of touching storage.
/// </summary>
internal sealed class RecordingUnitOfWork : IUnitOfWork
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly UnitOfWorkRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecordingUnitOfWork" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public RecordingUnitOfWork(UnitOfWorkRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        _recorder.Events.Enqueue("commit");
        return Task.CompletedTask;
    }
}

/// <summary>
///     A unit of work whose commit always fails, standing in for a concurrency conflict.
/// </summary>
internal sealed class ThrowingUnitOfWork : IUnitOfWork
{
    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("commit failed");
    }
}

/// <summary>
///     A command whose pipeline path is chosen by the test.
/// </summary>
internal sealed class UnitOfWorkCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the guard refuses the command.
    /// </summary>
    public bool ShouldDeny { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the validator reports the command malformed.
    /// </summary>
    public bool ShouldBeInvalid { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the shortcut answers the command.
    /// </summary>
    public bool ShouldAnswer { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the main handler throws.
    /// </summary>
    public bool ShouldThrow { get; set; }
}

/// <summary>
///     Fails when the command asks for it, so the test covers the fault path.
/// </summary>
internal sealed class UnitOfWorkCommandHandler : ICommandHandler<UnitOfWorkCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(UnitOfWorkCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.ShouldThrow)
        {
            throw new InvalidOperationException("handler failed");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
///     Refuses the command when the command asks for it.
/// </summary>
internal sealed class UnitOfWorkCommandGuard : ICommandGuard<UnitOfWorkCommand>
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(UnitOfWorkCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.ShouldDeny
            ? Task.FromResult(Verdict.Deny("not permitted", code: "NOT_PERMITTED"))
            : Task.FromResult(Verdict.Allow);
    }
}

/// <summary>
///     Reports the command malformed when the command asks for it.
/// </summary>
internal sealed class UnitOfWorkCommandValidator : ICommandValidator<UnitOfWorkCommand>
{
    /// <inheritdoc />
    public Task<Validity> ValidateAsync(UnitOfWorkCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.ShouldBeInvalid
            ? Task.FromResult(Validity.Invalid("name is required", member: "Name", code: "NAME_REQUIRED"))
            : Task.FromResult(Validity.Valid);
    }
}

/// <summary>
///     Answers the command without running the handler when the command asks for it.
/// </summary>
internal sealed class UnitOfWorkCommandShortcut : ICommandShortcut<UnitOfWorkCommand>
{
    /// <inheritdoc />
    public Task<Shortcut> TryAnswerAsync(UnitOfWorkCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.ShouldAnswer
            ? Task.FromResult(Shortcut.Answer("the work was already applied", code: "ALREADY_APPLIED"))
            : Task.FromResult(Shortcut.None);
    }
}

/// <summary>
///     A completion probe at the default priority, recording that it ran before the commit.
/// </summary>
internal sealed class EarlyCommandCompletionProbe : ICommandCompletionHandler<UnitOfWorkCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly UnitOfWorkRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EarlyCommandCompletionProbe" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public EarlyCommandCompletionProbe(UnitOfWorkRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<UnitOfWorkCommand> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.Events.Enqueue("early");
        return Task.CompletedTask;
    }
}

/// <summary>
///     A completion probe above the unit-of-work priority, recording that it ran after the commit.
/// </summary>
[HandlerPriority(HandlerPriorities.UnitOfWork + 100)]
internal sealed class LateCommandCompletionProbe : ICommandCompletionHandler<UnitOfWorkCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly UnitOfWorkRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LateCommandCompletionProbe" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public LateCommandCompletionProbe(UnitOfWorkRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<UnitOfWorkCommand> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.Events.Enqueue("late");
        return Task.CompletedTask;
    }
}
