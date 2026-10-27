using System.Collections.Concurrent;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UnitOfWork;

/// <summary>
///     Collects the commits and completion probes observed during one query unit-of-work test run.
/// </summary>
/// <remarks>
///     A distinct recorder type rather than a second <c>UnitOfWorkRecorder</c>, so the composition test that registers
///     different unit-of-work types on commands and queries can tell the two commits apart.
/// </remarks>
internal sealed class QueryUnitOfWorkRecorder
{
    /// <summary>
    ///     Gets the observed events, in the order they happened.
    /// </summary>
    public ConcurrentQueue<string> Events { get; } = new();
}

/// <summary>
///     A query unit of work that records each commit instead of touching storage.
/// </summary>
internal sealed class RecordingQueryUnitOfWork : IUnitOfWork
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly QueryUnitOfWorkRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecordingQueryUnitOfWork" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public RecordingQueryUnitOfWork(QueryUnitOfWorkRecorder recorder)
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
///     A query unit of work whose commit always fails, standing in for a concurrency conflict.
/// </summary>
internal sealed class ThrowingQueryUnitOfWork : IUnitOfWork
{
    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("commit failed");
    }
}

/// <summary>
///     A query whose pipeline path is chosen by the test.
/// </summary>
internal sealed class UnitOfWorkQuery : IQuery<string>
{
    /// <summary>
    ///     Gets or sets a value indicating whether the guard refuses the query.
    /// </summary>
    public bool ShouldDeny { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the validator reports the query malformed.
    /// </summary>
    public bool ShouldBeInvalid { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the shortcut answers the query.
    /// </summary>
    public bool ShouldAnswer { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the main handler throws.
    /// </summary>
    public bool ShouldThrow { get; set; }
}

/// <summary>
///     Returns a fixed result, or fails when the query asks for it.
/// </summary>
internal sealed class UnitOfWorkQueryHandler : IQueryHandler<UnitOfWorkQuery, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(UnitOfWorkQuery message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.ShouldThrow)
        {
            throw new InvalidOperationException("handler failed");
        }

        return Task.FromResult("produced");
    }
}

/// <summary>
///     Refuses the query when the query asks for it.
/// </summary>
internal sealed class UnitOfWorkQueryGuard : IQueryGuard<UnitOfWorkQuery>
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(UnitOfWorkQuery message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.ShouldDeny
            ? Task.FromResult(Verdict.Deny("not permitted", code: "NOT_PERMITTED"))
            : Task.FromResult(Verdict.Allow);
    }
}

/// <summary>
///     Reports the query malformed when the query asks for it.
/// </summary>
internal sealed class UnitOfWorkQueryValidator : IQueryValidator<UnitOfWorkQuery>
{
    /// <inheritdoc />
    public Task<Validity> ValidateAsync(UnitOfWorkQuery message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.ShouldBeInvalid
            ? Task.FromResult(Validity.Invalid("name is required", member: "Name", code: "NAME_REQUIRED"))
            : Task.FromResult(Validity.Valid);
    }
}

/// <summary>
///     Answers the query without running the handler when the query asks for it.
/// </summary>
internal sealed class UnitOfWorkQueryShortcut : IQueryShortcut<UnitOfWorkQuery, string>
{
    /// <inheritdoc />
    public Task<Shortcut<string>> TryAnswerAsync(UnitOfWorkQuery message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.ShouldAnswer
            ? Task.FromResult(Shortcut<string>.Answer("cached", reason: "served from the cache", code: "CACHE_HIT"))
            : Task.FromResult(Shortcut<string>.None);
    }
}

/// <summary>
///     A completion probe at the default priority, recording that it ran before the commit.
/// </summary>
internal sealed class EarlyQueryCompletionProbe : IQueryCompletionHandler<UnitOfWorkQuery>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly QueryUnitOfWorkRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EarlyQueryCompletionProbe" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public EarlyQueryCompletionProbe(QueryUnitOfWorkRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<UnitOfWorkQuery> context, CancellationToken cancellationToken)
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
internal sealed class LateQueryCompletionProbe : IQueryCompletionHandler<UnitOfWorkQuery>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly QueryUnitOfWorkRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LateQueryCompletionProbe" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public LateQueryCompletionProbe(QueryUnitOfWorkRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<UnitOfWorkQuery> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.Events.Enqueue("late");
        return Task.CompletedTask;
    }
}
