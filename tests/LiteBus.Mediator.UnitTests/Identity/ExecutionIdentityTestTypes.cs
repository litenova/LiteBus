using System.Collections.Concurrent;
using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.Identity;

/// <summary>
///     Records the execution identities that handlers and guards observe at each stage of a mediation.
/// </summary>
public sealed class IdentityRecorder
{
    /// <summary>
    ///     The observations in the order they were recorded.
    /// </summary>
    private readonly ConcurrentQueue<IdentityObservation> _observations = new();

    /// <summary>
    ///     Records the identity observed at one stage.
    /// </summary>
    /// <param name="stage">The stage that observed the identity, such as the handler or the guard.</param>
    /// <param name="identity">The observed identity.</param>
    public void Record(string stage, ExecutionIdentity identity)
    {
        _observations.Enqueue(new IdentityObservation(stage, identity));
    }

    /// <summary>
    ///     Gets the identities observed at one stage, in the order they were recorded.
    /// </summary>
    /// <param name="stage">The stage to read back.</param>
    /// <returns>The observed identities.</returns>
    public IReadOnlyList<ExecutionIdentity> ForStage(string stage)
    {
        return _observations.Where(observation => observation.Stage == stage).Select(observation => observation.Identity).ToList();
    }
}

/// <summary>
///     One identity observation: the stage that observed it and the identity itself.
/// </summary>
/// <param name="Stage">The stage that observed the identity.</param>
/// <param name="Identity">The observed identity.</param>
internal sealed record IdentityObservation(string Stage, ExecutionIdentity Identity);

/// <summary>
///     Supplies a fixed execution identity and counts how often the mediator asked for it.
/// </summary>
internal sealed class StubExecutionIdentityProvider : IExecutionIdentityProvider
{
    /// <summary>
    ///     The identity returned for every call.
    /// </summary>
    private readonly ExecutionIdentity _identity;

    /// <summary>
    ///     The number of times the mediator asked for the current identity.
    /// </summary>
    private int _callCount;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StubExecutionIdentityProvider" /> class.
    /// </summary>
    /// <param name="identity">The identity returned for every call.</param>
    public StubExecutionIdentityProvider(ExecutionIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        _identity = identity;
    }

    /// <summary>
    ///     Gets the number of times the mediator asked for the current identity.
    /// </summary>
    public int CallCount => Volatile.Read(ref _callCount);

    /// <inheritdoc />
    public ExecutionIdentity GetCurrentIdentity()
    {
        Interlocked.Increment(ref _callCount);
        return _identity;
    }
}

/// <summary>
///     A command whose handler starts a nested mediation, to prove the inner mediation inherits the outer identity.
/// </summary>
internal sealed class OuterIdentityCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the identity the nested mediation is sent with, or <see langword="null" /> to send it without one.
    /// </summary>
    public ExecutionIdentity? InnerIdentity { get; set; }
}

/// <summary>
///     Records the outer identity, then mediates <see cref="InnerIdentityCommand" /> inside the same ambient scope.
/// </summary>
internal sealed class OuterIdentityCommandHandler : ICommandHandler<OuterIdentityCommand>
{
    /// <summary>
    ///     The mediator used to start the nested mediation.
    /// </summary>
    private readonly ICommandMediator _mediator;

    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly IdentityRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OuterIdentityCommandHandler" /> class.
    /// </summary>
    /// <param name="mediator">The mediator used to start the nested mediation.</param>
    /// <param name="recorder">The recorder shared with the test.</param>
    public OuterIdentityCommandHandler(ICommandMediator mediator, IdentityRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(recorder);

        _mediator = mediator;
        _recorder = recorder;
    }

    /// <inheritdoc />
    public async Task HandleAsync(OuterIdentityCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _recorder.Record("outer", AmbientExecutionContext.Current.Identity);

        await _mediator.SendAsync(
            new InnerIdentityCommand(),
            new CommandMediationSettings { Identity = message.InnerIdentity },
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
///     A command whose handler records the identity its own mediation resolved.
/// </summary>
internal sealed class InnerIdentityCommand : ICommand;

/// <summary>
///     Records the identity of the mediation this handler runs in.
/// </summary>
internal sealed class InnerIdentityCommandHandler : ICommandHandler<InnerIdentityCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly IdentityRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InnerIdentityCommandHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public InnerIdentityCommandHandler(IdentityRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(InnerIdentityCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _recorder.Record("inner", AmbientExecutionContext.Current.Identity);

        return Task.CompletedTask;
    }
}

/// <summary>
///     A query whose handler records the identity its own mediation resolved.
/// </summary>
internal sealed class RecordIdentityQuery : IQuery<string>;

/// <summary>
///     Records the identity of the mediation this handler runs in.
/// </summary>
internal sealed class RecordIdentityQueryHandler : IQueryHandler<RecordIdentityQuery, string>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly IdentityRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecordIdentityQueryHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public RecordIdentityQueryHandler(IdentityRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<string> HandleAsync(RecordIdentityQuery message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _recorder.Record("query", AmbientExecutionContext.Current.Identity);

        return Task.FromResult("ok");
    }
}

/// <summary>
///     An event whose handler records the identity its own mediation resolved.
/// </summary>
internal sealed class RecordIdentityEvent : IEvent;

/// <summary>
///     Records the identity of the mediation this handler runs in.
/// </summary>
internal sealed class RecordIdentityEventHandler : IEventHandler<RecordIdentityEvent>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly IdentityRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecordIdentityEventHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public RecordIdentityEventHandler(IdentityRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(RecordIdentityEvent message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _recorder.Record("event", AmbientExecutionContext.Current.Identity);

        return Task.CompletedTask;
    }
}

/// <summary>
///     A command that declares an authorization policy, so a guard can observe the mediation identity.
/// </summary>
[Authorized("identity.observe")]
internal sealed class AuthorizedIdentityCommand : ICommand;

/// <summary>
///     Handles <see cref="AuthorizedIdentityCommand" />.
/// </summary>
internal sealed class AuthorizedIdentityCommandHandler : ICommandHandler<AuthorizedIdentityCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(AuthorizedIdentityCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     Records the mediation identity visible to a guard, then allows the message.
/// </summary>
/// <typeparam name="TCommand">The command type this guard runs for.</typeparam>
internal sealed class IdentityObservingAuthorizationGuard<TCommand> : AuthorizationGuard<TCommand>
    where TCommand : ICommand
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly IdentityRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="IdentityObservingAuthorizationGuard{TCommand}" /> class.
    /// </summary>
    /// <param name="metadata">Reads the declaration from the message type.</param>
    /// <param name="recorder">The recorder shared with the test.</param>
    public IdentityObservingAuthorizationGuard(IMessageMetadataAccessor metadata, IdentityRecorder recorder)
        : base(metadata)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        _recorder = recorder;
    }

    /// <inheritdoc />
    protected override Task<Verdict> AuthorizeAsync(string policy, TCommand message, CancellationToken cancellationToken)
    {
        _recorder.Record("guard", CurrentIdentity);

        return Task.FromResult(Verdict.Allow);
    }
}

/// <summary>
///     A command whose audit position is declared by attribute, so its record can be attributed from the principal.
/// </summary>
[Audited("identity.observe-action")]
internal sealed class AuditedIdentityCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the handler attributes the action itself.
    /// </summary>
    public bool HandlerAttributes { get; set; }
}

/// <summary>
///     Attributes the action itself only when the command asks it to, leaving attribution to the principal otherwise.
/// </summary>
internal sealed class AuditedIdentityCommandHandler : ICommandHandler<AuditedIdentityCommand>
{
    /// <summary>
    ///     The ambient audit scope.
    /// </summary>
    private readonly IAuditScope _audit;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditedIdentityCommandHandler" /> class.
    /// </summary>
    /// <param name="audit">The ambient audit scope.</param>
    public AuditedIdentityCommandHandler(IAuditScope audit)
    {
        ArgumentNullException.ThrowIfNull(audit);

        _audit = audit;
    }

    /// <inheritdoc />
    public Task HandleAsync(AuditedIdentityCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.HandlerAttributes)
        {
            _audit.WithActor(AuditActor.User("handler-user"));
        }

        return Task.CompletedTask;
    }
}

/// <summary>
///     Attributes every action to a fixed process, to prove a registered resolver wins over the principal.
/// </summary>
internal sealed class FixedAuditActorResolver : IAuditActorResolver
{
    /// <inheritdoc />
    public AuditActor? Resolve(MessageCompletionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return AuditActor.System("resolver-process");
    }
}

/// <summary>
///     An in-memory audit trail that records everything written to it.
/// </summary>
internal sealed class IdentityRecordingAuditTrail : IAuditTrail
{
    /// <summary>
    ///     Gets the records written during the test.
    /// </summary>
    public ConcurrentQueue<AuditRecord> Records { get; } = new();

    /// <inheritdoc />
    public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Records.Enqueue(record);
        return Task.CompletedTask;
    }
}
