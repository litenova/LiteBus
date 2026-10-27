using System.Collections.Concurrent;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.HandledAt;

/// <summary>
///     A clock the test sets by hand.
/// </summary>
internal sealed class StubTimeProvider : TimeProvider
{
    /// <summary>
    ///     Gets or sets the instant <see cref="GetUtcNow" /> answers with.
    /// </summary>
    public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        return UtcNow;
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        return TimeProvider.System.CreateTimer(callback, state, dueTime, period);
    }
}

/// <summary>
///     Collects the handled instants handlers observed during one test.
/// </summary>
internal sealed class HandledAtCapture
{
    /// <summary>
    ///     Gets the observed instants, in the order the handlers ran.
    /// </summary>
    public ConcurrentQueue<(string Name, DateTimeOffset HandledAt)> Observed { get; } = new();
}

/// <summary>
///     A command whose handler records its handled instant and optionally advances the clock or nests a mediation.
/// </summary>
internal sealed class HandledAtCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the handler advances the clock and then reads its instant again.
    /// </summary>
    public bool AdvanceClock { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the handler advances the clock and then sends a nested command.
    /// </summary>
    public bool SendNested { get; set; }
}

/// <summary>
///     Records the handled instant of its own mediation, then advances the clock or nests on request.
/// </summary>
internal sealed class HandledAtCommandHandler : ICommandHandler<HandledAtCommand>
{
    /// <summary>
    ///     The capture shared with the test.
    /// </summary>
    private readonly HandledAtCapture _capture;

    /// <summary>
    ///     The clock shared with the test.
    /// </summary>
    private readonly StubTimeProvider _clock;

    /// <summary>
    ///     The execution context of the mediation in flight.
    /// </summary>
    private readonly IExecutionContext _context;

    /// <summary>
    ///     The mediator used to send the nested command.
    /// </summary>
    private readonly ICommandMediator _mediator;

    /// <summary>
    ///     Initializes a new instance of the <see cref="HandledAtCommandHandler" /> class.
    /// </summary>
    /// <param name="context">The execution context of the mediation in flight.</param>
    /// <param name="clock">The clock shared with the test.</param>
    /// <param name="mediator">The mediator used to send the nested command.</param>
    /// <param name="capture">The capture shared with the test.</param>
    public HandledAtCommandHandler(
        IExecutionContext context,
        StubTimeProvider clock,
        ICommandMediator mediator,
        HandledAtCapture capture)
    {
        _context = context;
        _clock = clock;
        _mediator = mediator;
        _capture = capture;
    }

    /// <inheritdoc />
    public async Task HandleAsync(HandledAtCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _capture.Observed.Enqueue(("outer", _context.HandledAt));

        if (message.AdvanceClock)
        {
            _clock.UtcNow = _clock.UtcNow.AddHours(1);
            _capture.Observed.Enqueue(("outer-after-advance", _context.HandledAt));
        }

        if (message.SendNested)
        {
            _clock.UtcNow = _clock.UtcNow.AddHours(1);
            await _mediator.SendAsync(new HandledAtNestedCommand(), cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
///     A command sent from inside another mediation, proving a nested mediation reads its own instant.
/// </summary>
internal sealed class HandledAtNestedCommand : ICommand
{
}

/// <summary>
///     Records the handled instant of the nested mediation.
/// </summary>
internal sealed class HandledAtNestedCommandHandler : ICommandHandler<HandledAtNestedCommand>
{
    /// <summary>
    ///     The capture shared with the test.
    /// </summary>
    private readonly HandledAtCapture _capture;

    /// <summary>
    ///     The execution context of the nested mediation.
    /// </summary>
    private readonly IExecutionContext _context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="HandledAtNestedCommandHandler" /> class.
    /// </summary>
    /// <param name="context">The execution context of the nested mediation.</param>
    /// <param name="capture">The capture shared with the test.</param>
    public HandledAtNestedCommandHandler(IExecutionContext context, HandledAtCapture capture)
    {
        _context = context;
        _capture = capture;
    }

    /// <inheritdoc />
    public Task HandleAsync(HandledAtNestedCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _capture.Observed.Enqueue(("inner", _context.HandledAt));
        return Task.CompletedTask;
    }
}
