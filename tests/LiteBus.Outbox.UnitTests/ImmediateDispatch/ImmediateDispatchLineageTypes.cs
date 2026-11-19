using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Outbox.UnitTests.ImmediateDispatch;

/// <summary>
///     An event whose handler records the lineage its mediation observed.
/// </summary>
internal sealed class ImmediateLineageEvent : IEvent
{
}

/// <summary>
///     The lineage one reaction observed through the ambient execution context.
/// </summary>
/// <param name="MessageType">The runtime type of the message the observed mediation handles.</param>
/// <param name="Parent">The execution context of the enclosing mediation, or <see langword="null" /> for a root mediation.</param>
/// <param name="Cause">The committed fact that caused the mediation, or <see langword="null" /> when none was set.</param>
/// <param name="Identity">The identity the observed mediation ran for.</param>
/// <param name="Context">The execution context the handler observed.</param>
internal sealed record LineageObservation(
    Type? MessageType,
    IExecutionContext? Parent,
    MediationCause? Cause,
    ExecutionIdentity Identity,
    IExecutionContext Context);

/// <summary>
///     Records the lineage observations the probe handlers report and signals when a delivery lands.
/// </summary>
internal sealed class LineageRecorder
{
    /// <summary>
    ///     Gets the lineage observations the handlers recorded, in the order they ran.
    /// </summary>
    public List<LineageObservation> Observed { get; } = [];

    /// <summary>
    ///     Gets the signal completed when the first observation lands.
    /// </summary>
    public TaskCompletionSource Delivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     Records the lineage the given execution context carries.
    /// </summary>
    /// <param name="context">The execution context to observe.</param>
    public void Observe(IExecutionContext context)
    {
        Observed.Add(new LineageObservation(context.MessageType, context.Parent, context.Cause, context.Identity, context));
        Delivered.TrySetResult();
    }
}

/// <summary>
///     Records its lineage, proving the reaction reached its handler as a root mediation.
/// </summary>
internal sealed class ImmediateLineageEventHandler : IEventHandler<ImmediateLineageEvent>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly LineageRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ImmediateLineageEventHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public ImmediateLineageEventHandler(LineageRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(ImmediateLineageEvent message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _recorder.Observe(AmbientExecutionContext.Current);

        return Task.CompletedTask;
    }
}

/// <summary>
///     An execution context double standing in for the staging mediation's ambient scope.
/// </summary>
internal sealed class StubLineageContext : IExecutionContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="StubLineageContext" /> class.
    /// </summary>
    /// <param name="messageType">The runtime type of the message the staging mediation handles.</param>
    public StubLineageContext(Type? messageType)
    {
        MessageType = messageType;
    }

    /// <inheritdoc />
    public CancellationToken CancellationToken { get; }

    /// <inheritdoc />
    public DateTimeOffset HandledAt { get; }

    /// <inheritdoc />
    public ExecutionIdentity Identity => ExecutionIdentity.ForTenant("tenant-staging");

    /// <inheritdoc />
    public Type? MessageType { get; }

    /// <inheritdoc />
    public IExecutionContext? Parent => null;

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
///     Publishes the probe event with mediation settings built from the envelope, mirroring the in-process dispatcher.
/// </summary>
internal sealed class RecordingOutboxDispatcher : IOutboxDispatcher
{
    /// <summary>
    ///     The event mediator publishing the probe event.
    /// </summary>
    private readonly IEventMediator _mediator;

    /// <summary>
    ///     The cause source recorded on the settings.
    /// </summary>
    private readonly MediationCauseSource _source;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecordingOutboxDispatcher" /> class.
    /// </summary>
    /// <param name="mediator">The event mediator publishing the probe event.</param>
    /// <param name="source">The cause source recorded on the settings.</param>
    public RecordingOutboxDispatcher(IEventMediator mediator, MediationCauseSource source = MediationCauseSource.ImmediateDispatch)
    {
        _mediator = mediator;
        _source = source;
    }

    /// <inheritdoc />
    public Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var settings = new EventMediationSettings
        {
            Identity = ExecutionIdentity.ForTenant(message.TenantId),
            Cause = new MediationCause(
                message.CorrelationId ?? message.Id.ToString("N"),
                message.CausationId ?? message.Id.ToString("N"),
                _source)
        };

        return _mediator.PublishAsync(new ImmediateLineageEvent(), settings, cancellationToken);
    }
}

/// <summary>
///     Provides a discarded logger for processor dispatch calls in lineage tests.
/// </summary>
internal sealed class NullLineageLogger
{
    /// <summary>
    ///     Gets the discarded logger instance.
    /// </summary>
    public static ILogger Instance { get; } = NullLogger.Instance;
}

/// <summary>
///     Provides an empty dispatch scope, forcing the listener to fall back to its mediation services.
/// </summary>
internal sealed class EmptyDispatchScopeFactory : LiteBus.Runtime.Abstractions.IMessageDispatchScopeFactory
{
    /// <inheritdoc />
    public LiteBus.Runtime.Abstractions.IMessageDispatchScope CreateScope()
    {
        return new EmptyDispatchScope();
    }

    /// <summary>
    ///     A dispatch scope with no services registered.
    /// </summary>
    private sealed class EmptyDispatchScope : LiteBus.Runtime.Abstractions.IMessageDispatchScope
    {
        /// <summary>
        ///     Gets the empty scoped provider.
        /// </summary>
        public IServiceProvider ServiceProvider { get; } = new EmptyServiceProvider();

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     A service provider resolving nothing.
    /// </summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        /// <inheritdoc />
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}

/// <summary>
///     Denies a probe event nested under the staging scope, proving nesting rules see no leaked parent.
/// </summary>
internal sealed class DenyEventUnderStagingRule : IMediationNestingRule
{
    /// <summary>
    ///     Gets the code carried by the denial.
    /// </summary>
    public const string NestedUnderStaging = "test.nested-under-staging";

    /// <inheritdoc />
    public Verdict Evaluate(MediationNestingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.MessageType != typeof(ImmediateLineageEvent))
        {
            return Verdict.Allow;
        }

        foreach (var ancestor in context.Ancestors())
        {
            if (ancestor.MessageType == typeof(string))
            {
                return Verdict.Deny(
                    "The probe event must not observe the staging scope as an ancestor.",
                    NestedUnderStaging);
            }
        }

        return Verdict.Allow;
    }
}
