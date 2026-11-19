using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.Nesting;

/// <summary>
///     The nested mediation the probe command handler starts after recording its observation.
/// </summary>
internal enum NestingProbeAction
{
    /// <summary>
    ///     The handler records its observation and starts no nested mediation.
    /// </summary>
    None,

    /// <summary>
    ///     The handler sends the inner command.
    /// </summary>
    SendCommand,

    /// <summary>
    ///     The handler queries the probe query and records the answer.
    /// </summary>
    SendQuery,

    /// <summary>
    ///     The handler publishes the probe event.
    /// </summary>
    PublishEvent
}

/// <summary>
///     The nested mediation the probe event handler starts after recording its observation.
/// </summary>
public enum NestingEventAction
{
    /// <summary>
    ///     The handler records its observation and starts no nested mediation.
    /// </summary>
    None,

    /// <summary>
    ///     The handler sends the inner command.
    /// </summary>
    SendCommand,

    /// <summary>
    ///     The handler queries the probe query and records the answer.
    /// </summary>
    SendQuery
}

/// <summary>
///     A command whose handler records its lineage and then nests on request.
/// </summary>
internal sealed class NestingProbeCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the nested mediation the handler starts after recording its observation.
    /// </summary>
    public NestingProbeAction Action { get; set; } = NestingProbeAction.None;

    /// <summary>
    ///     Gets or sets a value indicating whether the handler ran for this command.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     A command sent from inside another mediation, proving nesting is allowed or denied.
/// </summary>
internal sealed class NestingInnerCommand : ICommand
{
}

/// <summary>
///     A query sent from inside another mediation, proving a nested query stays allowed.
/// </summary>
internal sealed class NestingProbeQuery : IQuery<string>
{
}

/// <summary>
///     An event published from inside another mediation, proving a nested event stays allowed.
/// </summary>
internal sealed class NestingProbeEvent : IEvent
{
}

/// <summary>
///     The lineage one handler observed through the ambient execution context.
/// </summary>
/// <param name="MessageType">The runtime type of the message the observed mediation handles.</param>
/// <param name="Ancestors">The message types of the enclosing mediations, nearest first.</param>
public sealed record LineageObservation(Type? MessageType, IReadOnlyList<Type?> Ancestors);

/// <summary>
///     Records the lineage observations and nested answers the probe handlers report.
/// </summary>
public sealed class NestingRecorder
{
    /// <summary>
    ///     Gets the lineage observations the handlers recorded, in the order they ran.
    /// </summary>
    public List<LineageObservation> Observed { get; } = [];

    /// <summary>
    ///     Gets the answers nested queries returned to their callers.
    /// </summary>
    public List<string> Answers { get; } = [];

    /// <summary>
    ///     Records the lineage the given execution context carries.
    /// </summary>
    /// <param name="context">The execution context to observe.</param>
    public void Observe(IExecutionContext context)
    {
        List<Type?> ancestors = [];

        for (var current = context.Parent; current is not null; current = current.Parent)
        {
            ancestors.Add(current.MessageType);
        }

        Observed.Add(new LineageObservation(context.MessageType, [.. ancestors]));
    }
}

/// <summary>
///     The options the probe event handler reads, shared with the test.
/// </summary>
public sealed class NestingProbeOptions
{
    /// <summary>
    ///     Gets or sets the nested mediation the probe event handler starts after recording its observation.
    /// </summary>
    public NestingEventAction EventAction { get; set; } = NestingEventAction.None;
}

/// <summary>
///     Records its lineage, then starts the nested mediation the command names.
/// </summary>
internal sealed class NestingProbeCommandHandler : ICommandHandler<NestingProbeCommand>
{
    /// <summary>
    ///     The mediator used to send the nested command.
    /// </summary>
    private readonly ICommandMediator _commands;

    /// <summary>
    ///     The mediator used to send the nested query.
    /// </summary>
    private readonly IQueryMediator _queries;

    /// <summary>
    ///     The mediator used to publish the nested event.
    /// </summary>
    private readonly IEventMediator _events;

    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly NestingRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="NestingProbeCommandHandler" /> class.
    /// </summary>
    /// <param name="commands">The mediator used to send the nested command.</param>
    /// <param name="queries">The mediator used to send the nested query.</param>
    /// <param name="events">The mediator used to publish the nested event.</param>
    /// <param name="recorder">The recorder shared with the test.</param>
    public NestingProbeCommandHandler(
        ICommandMediator commands,
        IQueryMediator queries,
        IEventMediator events,
        NestingRecorder recorder)
    {
        _commands = commands;
        _queries = queries;
        _events = events;
        _recorder = recorder;
    }

    /// <inheritdoc />
    public async Task HandleAsync(NestingProbeCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        message.HandlerRan = true;
        _recorder.Observe(AmbientExecutionContext.Current);

        switch (message.Action)
        {
            case NestingProbeAction.SendCommand:
                await _commands.SendAsync(new NestingInnerCommand(), cancellationToken).ConfigureAwait(false);
                break;
            case NestingProbeAction.SendQuery:
                var answer = await _queries.QueryAsync<string>(new NestingProbeQuery(), cancellationToken).ConfigureAwait(false);
                _recorder.Answers.Add(answer);
                break;
            case NestingProbeAction.PublishEvent:
                await _events.PublishAsync(new NestingProbeEvent(), cancellationToken).ConfigureAwait(false);
                break;
            default:
                break;
        }
    }
}

/// <summary>
///     Records its lineage, proving the inner command reached its handler.
/// </summary>
internal sealed class NestingInnerCommandHandler : ICommandHandler<NestingInnerCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly NestingRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="NestingInnerCommandHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public NestingInnerCommandHandler(NestingRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(NestingInnerCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _recorder.Observe(AmbientExecutionContext.Current);

        return Task.CompletedTask;
    }
}

/// <summary>
///     Records its lineage and answers, proving the nested query reached its handler.
/// </summary>
internal sealed class NestingProbeQueryHandler : IQueryHandler<NestingProbeQuery, string>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly NestingRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="NestingProbeQueryHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public NestingProbeQueryHandler(NestingRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <summary>
    ///     Gets the answer the handler returns.
    /// </summary>
    public const string Answer = "probe-answer";

    /// <inheritdoc />
    public Task<string> HandleAsync(NestingProbeQuery message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _recorder.Observe(AmbientExecutionContext.Current);

        return Task.FromResult(Answer);
    }
}

/// <summary>
///     Records its lineage, then starts the nested mediation the shared options name.
/// </summary>
internal sealed class NestingProbeEventHandler : IEventHandler<NestingProbeEvent>
{
    /// <summary>
    ///     The mediator used to send the nested command.
    /// </summary>
    private readonly ICommandMediator _commands;

    /// <summary>
    ///     The mediator used to send the nested query.
    /// </summary>
    private readonly IQueryMediator _queries;

    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly NestingRecorder _recorder;

    /// <summary>
    ///     The options shared with the test.
    /// </summary>
    private readonly NestingProbeOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="NestingProbeEventHandler" /> class.
    /// </summary>
    /// <param name="commands">The mediator used to send the nested command.</param>
    /// <param name="queries">The mediator used to send the nested query.</param>
    /// <param name="recorder">The recorder shared with the test.</param>
    /// <param name="options">The options shared with the test.</param>
    public NestingProbeEventHandler(
        ICommandMediator commands,
        IQueryMediator queries,
        NestingRecorder recorder,
        NestingProbeOptions options)
    {
        _commands = commands;
        _queries = queries;
        _recorder = recorder;
        _options = options;
    }

    /// <inheritdoc />
    public async Task HandleAsync(NestingProbeEvent message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _recorder.Observe(AmbientExecutionContext.Current);

        switch (_options.EventAction)
        {
            case NestingEventAction.SendCommand:
                await _commands.SendAsync(new NestingInnerCommand(), cancellationToken).ConfigureAwait(false);
                break;
            case NestingEventAction.SendQuery:
                var answer = await _queries.QueryAsync<string>(new NestingProbeQuery(), cancellationToken).ConfigureAwait(false);
                _recorder.Answers.Add(answer);
                break;
            default:
                break;
        }
    }
}

/// <summary>
///     An application nesting rule that denies a nested probe query, proving the rule port.
/// </summary>
internal sealed class DenyNestedQueriesRule : IMediationNestingRule
{
    /// <summary>
    ///     Gets the code carried by the denial.
    /// </summary>
    public const string NestedQuery = "test.nested-query";

    /// <inheritdoc />
    public Verdict Evaluate(MediationNestingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.MessageType == typeof(NestingProbeQuery))
        {
            return Verdict.Deny(
                $"Query '{nameof(NestingProbeQuery)}' is denied by the test rule.",
                NestedQuery);
        }

        return Verdict.Allow;
    }
}

/// <summary>
///     An execution context double with a fixed message type and parent, for nesting unit tests.
/// </summary>
internal sealed class StubExecutionContext : IExecutionContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="StubExecutionContext" /> class.
    /// </summary>
    /// <param name="messageType">The runtime type of the message this mediation handles.</param>
    /// <param name="parent">The execution context of the enclosing mediation.</param>
    public StubExecutionContext(Type? messageType = null, IExecutionContext? parent = null)
    {
        MessageType = messageType;
        Parent = parent;
    }

    /// <inheritdoc />
    public CancellationToken CancellationToken { get; }

    /// <inheritdoc />
    public DateTimeOffset HandledAt { get; }

    /// <inheritdoc />
    public Type? MessageType { get; }

    /// <inheritdoc />
    public IExecutionContext? Parent { get; }

    /// <inheritdoc />
    public IDictionary<string, object> Items { get; } = new Dictionary<string, object>();

    /// <inheritdoc />
    public IHandleContextData Data { get; } = new HandleContextData();

    /// <inheritdoc />
    public IReadOnlyCollection<string> Tags { get; } = [];

    /// <inheritdoc />
    public object? MessageResult { get; set; }

    /// <inheritdoc />
    public bool PostHandlersSuppressed { get; private set; }

    /// <inheritdoc />
    public void SuppressPostHandlers()
    {
        PostHandlersSuppressed = true;
    }
}

/// <summary>
///     A command whose handler records the cause, parent, identity, and message type it observed.
/// </summary>
internal sealed class CauseProbeCommand : ICommand
{
}

/// <summary>
///     The lineage one handler observed, including the cause when the mediation carried one.
/// </summary>
/// <param name="MessageType">The runtime type of the message the observed mediation handles.</param>
/// <param name="Parent">The execution context of the enclosing mediation, or <see langword="null" /> for a root mediation.</param>
/// <param name="Cause">The committed fact that caused the mediation, or <see langword="null" /> when none was set.</param>
/// <param name="Identity">The identity the observed mediation ran for.</param>
public sealed record CauseObservation(
    Type? MessageType,
    IExecutionContext? Parent,
    MediationCause? Cause,
    ExecutionIdentity Identity);

/// <summary>
///     Records the cause observations the probe handlers report.
/// </summary>
public sealed class CauseRecorder
{
    /// <summary>
    ///     Gets the observations the handlers recorded, in the order they ran.
    /// </summary>
    public List<CauseObservation> Observed { get; } = [];

    /// <summary>
    ///     Records the lineage the given execution context carries.
    /// </summary>
    /// <param name="context">The execution context to observe.</param>
    public void Observe(IExecutionContext context)
    {
        Observed.Add(new CauseObservation(context.MessageType, context.Parent, context.Cause, context.Identity));
    }
}

/// <summary>
///     Records the cause, parent, identity, and message type of its mediation.
/// </summary>
internal sealed class CauseProbeCommandHandler : ICommandHandler<CauseProbeCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CauseRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CauseProbeCommandHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public CauseProbeCommandHandler(CauseRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(CauseProbeCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _recorder.Observe(AmbientExecutionContext.Current);

        return Task.CompletedTask;
    }
}
