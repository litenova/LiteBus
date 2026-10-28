using System;
using System.Collections.Generic;
using System.Threading;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Audit;

/// <summary>
///     An <see cref="IAuditScope" /> backed by the ambient execution context, so what a handler pushes is visible to the
///     completion stage that writes the record.
/// </summary>
/// <remarks>
///     The scope holds no state of its own. It reads and writes <see cref="IExecutionContext.Items" />, which flows with
///     the mediation across await points and is discarded when the mediation ends. This makes the scope safe to register
///     as a singleton and correct under concurrency, because two mediations never share an execution context. Parallel
///     event handlers sharing one mediation share one scope state: the first write wins the creation race, later writes
///     land in the same state, and two handlers racing to set the same property leave whichever landed last.
/// </remarks>
internal sealed class AmbientAuditScope : IAuditScope
{
    /// <summary>
    ///     The execution-context item key under which the mutable scope state is stored.
    /// </summary>
    internal const string ItemKey = "__LiteBus.Audit.Scope";

    /// <summary>
    ///     Guards the check-then-set in <see cref="GetOrCreate" /> against parallel event handlers.
    /// </summary>
    private static readonly Lock Gate = new();

    /// <inheritdoc />
    public AuditActor? Actor => Find()?.Actor;

    /// <inheritdoc />
    public string? TargetId => Find()?.TargetId;

    /// <inheritdoc />
    public string? Reason => Find()?.Reason;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Properties =>
        (IReadOnlyDictionary<string, string>?) Find()?.Properties ?? new Dictionary<string, string>(StringComparer.Ordinal);

    /// <inheritdoc />
    public IAuditScope WithActor(AuditActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        GetOrCreate().Actor = actor;
        return this;
    }

    /// <inheritdoc />
    public IAuditScope WithTarget(string targetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        GetOrCreate().TargetId = targetId;
        return this;
    }

    /// <inheritdoc />
    public IAuditScope WithReason(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        GetOrCreate().Reason = reason;
        return this;
    }

    /// <inheritdoc />
    public IAuditScope WithProperty(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        GetOrCreate().Properties[name] = value;
        return this;
    }

    /// <summary>
    ///     Reads the scope state recorded for the current mediation, if any.
    /// </summary>
    /// <returns>The state, or <see langword="null" /> when nothing was pushed or no mediation is in flight.</returns>
    internal static AuditScopeState? Find()
    {
        var executionContext = AmbientExecutionContext.GetCurrentOrDefault();

        if (executionContext is null)
        {
            return null;
        }

        return executionContext.Items.TryGetValue(ItemKey, out var stored) ? stored as AuditScopeState : null;
    }

    /// <summary>
    ///     Reads the scope state for the current mediation, creating it on first use.
    /// </summary>
    /// <returns>The mutable scope state.</returns>
    /// <exception cref="NoExecutionContextException">Thrown when no mediation is in flight.</exception>
    private static AuditScopeState GetOrCreate()
    {
        var executionContext = AmbientExecutionContext.Current;

        lock (Gate)
        {
            if (executionContext.Items.TryGetValue(ItemKey, out var stored) && stored is AuditScopeState state)
            {
                return state;
            }

            // Two parallel event handlers reaching here together must share one state: creating two and letting one
            // overwrite the other would lose whatever the loser pushed.
            var created = new AuditScopeState();
            executionContext.Items[ItemKey] = created;
            return created;
        }
    }
}
