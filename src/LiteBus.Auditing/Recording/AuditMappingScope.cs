using System;
using System.Threading;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Auditing.Recording;

/// <summary>
///     Carries the mapping context of the mediation whose record is being written to the audit trail, from the record
///     writer to the store trail that maps the record.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="IAuditTrail.WriteAsync" /> receives only the record, so the built-in record writer and the command
///         batch participant enter this scope around the trail call. The store trail runs in the same asynchronous
///         flow and reads it with <see cref="Resolve" />.
///     </para>
///     <para>
///         A record written with no scope entered, such as one delivered through the outbox capture mode or written
///         to the trail directly by application code, is mapped with a context built from the scope that writes it.
///     </para>
/// </remarks>
internal static class AuditMappingScope
{
    /// <summary>
    ///     The mapping context of the record being written in the current asynchronous flow.
    /// </summary>
    private static readonly AsyncLocal<AuditMappingContext?> Current = new();

    /// <summary>
    ///     Makes a mapping context current until the returned handle is disposed.
    /// </summary>
    /// <param name="context">The mapping context of the record about to be written.</param>
    /// <returns>A handle that restores the previous context when disposed.</returns>
    public static IDisposable Enter(AuditMappingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var previous = Current.Value;
        Current.Value = context;

        return new Restore(previous);
    }

    /// <summary>
    ///     Creates the mapping context of a mediation that is ending.
    /// </summary>
    /// <param name="context">The completion context of the mediation.</param>
    /// <param name="services">The mediation's scope.</param>
    /// <param name="clock">The clock the mediation runs on.</param>
    /// <returns>The mapping context.</returns>
    public static AuditMappingContext ForMediation(
        MessageCompletionContext context,
        IServiceProvider services,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clock);

        return new AuditMappingContext(
            new MessageEvaluationContext(
                context.Message,
                context.Message.GetType(),
                context.Identity,
                services,
                clock,
                clock.GetUtcNow())
            {
                ExecutionContext = AmbientExecutionContext.GetCurrentOrDefault()
            });
    }

    /// <summary>
    ///     Gets the mapping context for a record a store trail is about to map, with a fresh feature collection.
    /// </summary>
    /// <param name="record">The record being written.</param>
    /// <param name="services">The scope of the store trail, used when no mediation context is current.</param>
    /// <returns>The mapping context.</returns>
    public static AuditMappingContext Resolve(AuditRecord record, IServiceProvider? services)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (Current.Value is { } current)
        {
            return current with { Features = new AuditMappingFeatures() };
        }

        var provider = services ?? EmptyServiceProvider.Instance;
        var clock = provider.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
        var executionContext = AmbientExecutionContext.GetCurrentOrDefault();

        return new AuditMappingContext(
            new MessageEvaluationContext(
                record,
                typeof(AuditRecord),
                executionContext?.Identity ?? ExecutionIdentity.Anonymous,
                provider,
                clock,
                record.OccurredAt)
            {
                ExecutionContext = executionContext
            });
    }

    /// <summary>
    ///     Restores the previous mapping context when disposed.
    /// </summary>
    private sealed class Restore : IDisposable
    {
        /// <summary>
        ///     The context that was current before the scope was entered.
        /// </summary>
        private readonly AuditMappingContext? _previous;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Restore" /> class.
        /// </summary>
        /// <param name="previous">The context to restore.</param>
        public Restore(AuditMappingContext? previous)
        {
            _previous = previous;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Current.Value = _previous;
        }
    }

    /// <summary>
    ///     A service provider that resolves nothing, for a trail constructed without one.
    /// </summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        /// <summary>
        ///     Gets the shared instance.
        /// </summary>
        public static EmptyServiceProvider Instance { get; } = new();

        /// <inheritdoc />
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
