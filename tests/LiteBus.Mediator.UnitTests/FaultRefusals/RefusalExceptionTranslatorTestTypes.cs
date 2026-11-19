using System.Runtime.CompilerServices;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.FaultRefusals;

/// <summary>
///     A scoped service a translator reads while translating, standing in for localization or tenant-aware detail.
/// </summary>
internal sealed class TranslatorDetailProvider
{
#pragma warning disable CA1822 // The translator reads a scoped instance; keeping the instance shape is what the test proves.
    /// <summary>
    ///     Reads the detail the translator reports as the refusal reason.
    /// </summary>
    /// <param name="cancellationToken">The token signalled when translation is cancelled.</param>
    /// <returns>The detail for the current mediation.</returns>
    public Task<string> GetDetailAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult("the reference is already taken for this tenant");
    }
#pragma warning restore CA1822
}

/// <summary>
///     Translates the test conflict into a denial.
/// </summary>
internal sealed class ConflictTranslator : IRefusalExceptionTranslator
{
    /// <inheritdoc />
    public ValueTask<Refusal?> TranslateAsync(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is ConflictException)
        {
            return ValueTask.FromResult<Refusal?>(Refusal.Denied("the reference is already taken", "CONFLICT"));
        }

        return ValueTask.FromResult<Refusal?>(null);
    }
}

/// <summary>
///     Translates the invalid-shaped test fault into invalid input.
/// </summary>
internal sealed class InvalidDomainTranslator : IRefusalExceptionTranslator
{
    /// <inheritdoc />
    public ValueTask<Refusal?> TranslateAsync(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is InvalidFaultException)
        {
            return ValueTask.FromResult<Refusal?>(Refusal.Invalid("the operation is not acceptable", "NOT_ACCEPTABLE"));
        }

        return ValueTask.FromResult<Refusal?>(null);
    }
}

/// <summary>
///     Translates the test conflict with a caller-supplied code, so order tests can tell two matches apart.
/// </summary>
internal sealed class FixedCodeTranslator : IRefusalExceptionTranslator
{
    /// <summary>
    ///     The code reported for a translated conflict.
    /// </summary>
    private readonly string _code;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FixedCodeTranslator" /> class.
    /// </summary>
    /// <param name="code">The code reported for a translated conflict.</param>
    public FixedCodeTranslator(string code)
    {
        _code = code;
    }

    /// <inheritdoc />
    public ValueTask<Refusal?> TranslateAsync(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is ConflictException)
        {
            return ValueTask.FromResult<Refusal?>(Refusal.Denied($"translated by {_code}", _code));
        }

        return ValueTask.FromResult<Refusal?>(null);
    }
}

/// <summary>
///     Records every fault it is offered and translates all of them, proving what is never offered.
/// </summary>
internal sealed class RecordingCatchAllTranslator : IRefusalExceptionTranslator
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly FaultRefusalRecorder _calls;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecordingCatchAllTranslator" /> class.
    /// </summary>
    /// <param name="calls">The recorder shared with the test.</param>
    public RecordingCatchAllTranslator(FaultRefusalRecorder calls)
    {
        _calls = calls;
    }

    /// <inheritdoc />
    public ValueTask<Refusal?> TranslateAsync(Exception exception, CancellationToken cancellationToken)
    {
        _calls.Calls.Enqueue($"offered:{exception.GetType().Name}");

        return ValueTask.FromResult<Refusal?>(Refusal.Denied("caught everything", "CAUGHT"));
    }
}

/// <summary>
///     Translates nothing, proving an unrecognized fault keeps the failure path.
/// </summary>
internal sealed class NullTranslator : IRefusalExceptionTranslator
{
    /// <inheritdoc />
    public ValueTask<Refusal?> TranslateAsync(Exception exception, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<Refusal?>(null);
    }
}

/// <summary>
///     Translates the test conflict into a denial whose reason comes from a scoped service.
/// </summary>
internal sealed class ScopedDetailTranslator : IRefusalExceptionTranslator
{
    /// <summary>
    ///     The scoped provider the refusal reason is read from.
    /// </summary>
    private readonly TranslatorDetailProvider _details;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ScopedDetailTranslator" /> class.
    /// </summary>
    /// <param name="details">The scoped provider the refusal reason is read from.</param>
    public ScopedDetailTranslator(TranslatorDetailProvider details)
    {
        _details = details;
    }

    /// <inheritdoc />
    public async ValueTask<Refusal?> TranslateAsync(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ConflictException)
        {
            return null;
        }

        var detail = await _details.GetDetailAsync(cancellationToken).ConfigureAwait(false);

        return Refusal.Denied(detail, "CONFLICT");
    }
}

/// <summary>
///     A command whose handler raises a message fault when the test asks for it.
/// </summary>
internal sealed class FaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler raises a message fault.
    /// </summary>
    public bool ThrowFault { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the guard raises a message fault.
    /// </summary>
    public bool GuardThrows { get; set; }
}

/// <summary>
///     Raises a message fault when the command asks for it.
/// </summary>
internal sealed class FaultCommandHandler : ICommandHandler<FaultCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly FaultRefusalRecorder _calls;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FaultCommandHandler" /> class.
    /// </summary>
    /// <param name="calls">The recorder shared with the test.</param>
    public FaultCommandHandler(FaultRefusalRecorder calls)
    {
        _calls = calls;
    }

    /// <inheritdoc />
    public Task HandleAsync(FaultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _calls.Calls.Enqueue("main");

        if (message.ThrowFault)
        {
            throw new MessageFaultException(typeof(FaultCommand), "LOOKUP_MISSING", "the permission lookup is not registered");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
///     Raises a message fault when the command asks for it, standing in for a policy that reports a fault result.
/// </summary>
internal sealed class FaultCommandGuard : ICommandGuard<FaultCommand>
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(FaultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.GuardThrows)
        {
            throw new MessageFaultException(typeof(FaultCommand), "LOOKUP_MISSING", "the permission lookup is not registered");
        }

        return Task.FromResult(Verdict.Allow);
    }
}

/// <summary>
///     Records that it ran, so a test can prove a message fault never reaches the error stage.
/// </summary>
internal sealed class FaultErrorHandler : ICommandErrorHandler<FaultCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly FaultRefusalRecorder _calls;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FaultErrorHandler" /> class.
    /// </summary>
    /// <param name="calls">The recorder shared with the test.</param>
    public FaultErrorHandler(FaultRefusalRecorder calls)
    {
        _calls = calls;
    }

    /// <inheritdoc />
    public Task HandleErrorAsync(MessageErrorContext<FaultCommand, object> context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        _calls.Calls.Enqueue("error");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Records the completion context so the test can assert which outcome a message fault reported.
/// </summary>
internal sealed class FaultCompletionHandler : ICommandCompletionHandler<FaultCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CompletionRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FaultCompletionHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public FaultCompletionHandler(CompletionRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<FaultCommand> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.Observed.Enqueue(("fault", context.AsUntyped()));
        return Task.CompletedTask;
    }
}

/// <summary>
///     An audited command whose handler raises a message fault when the test asks for it.
/// </summary>
[Audited("test.message-fault")]
internal sealed class AuditedFaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler raises a message fault.
    /// </summary>
    public bool ThrowFault { get; set; }
}

/// <summary>
///     Raises a message fault when the audited command asks for it.
/// </summary>
internal sealed class AuditedFaultCommandHandler : ICommandHandler<AuditedFaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(AuditedFaultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.ThrowFault)
        {
            throw new MessageFaultException(typeof(AuditedFaultCommand), "LOOKUP_MISSING", "the permission lookup is not registered");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
///     A stream query whose source throws the fault the test asks for after yielding its items.
/// </summary>
internal sealed class ConflictStreamQuery : IStreamQuery<int>
{
    /// <summary>
    ///     Gets or sets the number of items the source stream yields before it faults.
    /// </summary>
    public int SourceItems { get; set; } = 2;

    /// <summary>
    ///     Gets or sets a value indicating whether the source throws the recognized conflict.
    /// </summary>
    public bool ThrowConflict { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the source raises a message fault.
    /// </summary>
    public bool ThrowFault { get; set; }
}

/// <summary>
///     Yields the items the query asked for, then throws the fault it asked for.
/// </summary>
internal sealed class ConflictStreamQueryHandler : IStreamQueryHandler<ConflictStreamQuery, int>
{
    /// <inheritdoc />
    public async IAsyncEnumerable<int> StreamAsync(
        ConflictStreamQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        for (var index = 0; index < query.SourceItems; index++)
        {
            await Task.Yield();
            yield return index;
        }

        if (query.ThrowConflict)
        {
            throw new ConflictException("the stream hit a conflict");
        }

        if (query.ThrowFault)
        {
            throw new MessageFaultException(typeof(ConflictStreamQuery), "LOOKUP_MISSING", "the permission lookup is not registered");
        }
    }
}

/// <summary>
///     Records the completion context so the test can assert which outcome a stream fault reported.
/// </summary>
internal sealed class ConflictStreamCompletionHandler : IQueryCompletionHandler<ConflictStreamQuery>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CompletionRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ConflictStreamCompletionHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public ConflictStreamCompletionHandler(CompletionRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<ConflictStreamQuery> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.Observed.Enqueue(("conflict-stream", context.AsUntyped()));
        return Task.CompletedTask;
    }
}
