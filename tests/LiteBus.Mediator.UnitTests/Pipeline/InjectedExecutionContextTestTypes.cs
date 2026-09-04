using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     A command whose handler takes the execution context as a constructor dependency.
/// </summary>
internal sealed class InjectedContextCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the note the handler records through the injected context.
    /// </summary>
    public string Note { get; set; } = string.Empty;
}

/// <summary>
///     Records what the handler observed through the injected context.
/// </summary>
public sealed class InjectedContextRecorder
{
    /// <summary>
    ///     The distinct context instances observed, compared by reference.
    /// </summary>
    private readonly List<IExecutionContext> _contexts = [];

    /// <summary>
    ///     Gets the notes read back from each mediation's own context.
    /// </summary>
    public List<string> Notes { get; } = [];

    /// <summary>
    ///     Gets a value indicating whether every observed context was the instance the ambient static returned.
    /// </summary>
    public bool MatchedAmbient { get; private set; } = true;

    /// <summary>
    ///     Gets the number of distinct context instances observed.
    /// </summary>
    public int DistinctContexts => _contexts.Count;

    /// <summary>
    ///     Records one observation.
    /// </summary>
    /// <param name="context">The context the handler was given.</param>
    /// <param name="note">The note read back from that context.</param>
    public void Record(IExecutionContext context, string note)
    {
        Notes.Add(note);
        MatchedAmbient &= ReferenceEquals(context, AmbientExecutionContext.Current);

        if (!_contexts.Exists(observed => ReferenceEquals(observed, context)))
        {
            _contexts.Add(context);
        }
    }
}

/// <summary>
///     Writes to the injected context and reads the value back, proving the instance belongs to this mediation.
/// </summary>
internal sealed class InjectedContextCommandHandler : ICommandHandler<InjectedContextCommand>
{
    /// <summary>
    ///     The execution context of the mediation this handler was resolved for.
    /// </summary>
    private readonly IExecutionContext _context;

    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly InjectedContextRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InjectedContextCommandHandler" /> class.
    /// </summary>
    /// <param name="context">The execution context, resolved from the dispatch scope.</param>
    /// <param name="recorder">The recorder shared with the test.</param>
    public InjectedContextCommandHandler(IExecutionContext context, InjectedContextRecorder recorder)
    {
        _context = context;
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(InjectedContextCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _context.Data.Set(new HandlerNote(message.Note));
        _recorder.Record(_context, _context.Data.Get<HandlerNote>().Value);

        return Task.CompletedTask;
    }
}

/// <summary>
///     A note written into and read back out of the execution context data store.
/// </summary>
/// <param name="Value">The note text.</param>
internal sealed record HandlerNote(string Value);
