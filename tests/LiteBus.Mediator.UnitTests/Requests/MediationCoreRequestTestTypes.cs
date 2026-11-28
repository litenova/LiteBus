using System.Collections.Concurrent;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Requests;

/// <summary>
///     A command whose handler records the cause and parent its execution context carries.
/// </summary>
internal sealed class RequestCauseCommand : ICommand
{
}

/// <summary>
///     The lineage one handler observed, including the cause when the mediation carried one.
/// </summary>
/// <param name="Cause">The committed fact that caused the mediation, or <see langword="null" /> when none was set.</param>
/// <param name="Parent">The execution context of the enclosing mediation, or <see langword="null" /> for a root mediation.</param>
public sealed record RequestCauseObservation(MediationCause? Cause, IExecutionContext? Parent);

/// <summary>
///     Records the cause observations the probe handlers report.
/// </summary>
public sealed class RequestCauseRecorder
{
    /// <summary>
    ///     Gets the observations the handlers recorded, in the order they ran.
    /// </summary>
    public List<RequestCauseObservation> Observed { get; } = [];

    /// <summary>
    ///     Records the lineage the given execution context carries.
    /// </summary>
    /// <param name="context">The execution context to observe.</param>
    public void Observe(IExecutionContext context)
    {
        Observed.Add(new RequestCauseObservation(context.Cause, context.Parent));
    }
}

/// <summary>
///     Records the cause and parent of its mediation from its injected execution context.
/// </summary>
internal sealed class RequestCauseCommandHandler : ICommandHandler<RequestCauseCommand>
{
    /// <summary>
    ///     The execution context of the mediation in flight.
    /// </summary>
    private readonly IExecutionContext _executionContext;

    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly RequestCauseRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RequestCauseCommandHandler" /> class.
    /// </summary>
    /// <param name="executionContext">The execution context of the mediation in flight.</param>
    /// <param name="recorder">The recorder shared with the test.</param>
    public RequestCauseCommandHandler(IExecutionContext executionContext, RequestCauseRecorder recorder)
    {
        _executionContext = executionContext;
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(RequestCauseCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _recorder.Observe(_executionContext);

        return Task.CompletedTask;
    }
}

/// <summary>
///     The domain fault the test translator recognizes as a denial.
/// </summary>
internal sealed class RequestConflictException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RequestConflictException" /> class.
    /// </summary>
    /// <param name="message">The message describing the conflict.</param>
    public RequestConflictException(string message)
        : base(message)
    {
    }
}

/// <summary>
///     A command whose handler raises the domain fault the test translator recognizes.
/// </summary>
internal sealed class RequestConflictCommand : ICommand
{
}

/// <summary>
///     Raises the domain fault the test translator recognizes.
/// </summary>
internal sealed class RequestConflictCommandHandler : ICommandHandler<RequestConflictCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(RequestConflictCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        throw new RequestConflictException("the reference is already taken");
    }
}

/// <summary>
///     Translates the test conflict into a denial.
/// </summary>
internal sealed class RequestConflictTranslator : IRefusalExceptionTranslator
{
    /// <inheritdoc />
    public ValueTask<Refusal?> TranslateAsync(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is RequestConflictException)
        {
            return ValueTask.FromResult<Refusal?>(Refusal.Denied("the reference is already taken", "CONFLICT"));
        }

        return ValueTask.FromResult<Refusal?>(null);
    }
}

/// <summary>
///     Collects the exception type names one catch-all classifier was offered.
/// </summary>
public sealed class RequestOfferRecorder
{
    /// <summary>
    ///     Gets the offered exception type names, in the order they were offered.
    /// </summary>
    public ConcurrentQueue<string> Offered { get; } = new();
}

/// <summary>
///     Records every fault it is offered and classifies all of them, proving what is never offered.
/// </summary>
internal sealed class RequestCatchAllClassifier : IFaultRefusalClassifier
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly RequestOfferRecorder _offers;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RequestCatchAllClassifier" /> class.
    /// </summary>
    /// <param name="offers">The recorder shared with the test.</param>
    public RequestCatchAllClassifier(RequestOfferRecorder offers)
    {
        _offers = offers;
    }

    /// <inheritdoc />
    public bool TryClassify(Exception exception, out Refusal refusal)
    {
        _offers.Offered.Enqueue(exception.GetType().Name);
        refusal = Refusal.Denied("caught everything", "CAUGHT");
        return true;
    }
}
