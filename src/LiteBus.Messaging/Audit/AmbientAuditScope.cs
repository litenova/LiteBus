using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Audit;

/// <summary>
///     An <see cref="IAuditScope" /> backed by the ambient execution context, so what a handler pushes is visible to the
///     completion stage that writes the record.
/// </summary>
/// <remarks>
///     The scope holds no state of its own. It reads and writes <see cref="IExecutionContext.Items" />, which flows with
///     the mediation across await points and is discarded when the mediation ends. This makes the scope safe to register
///     as a singleton and correct under concurrency, because two mediations never share an execution context.
/// </remarks>
internal sealed class AmbientAuditScope : IAuditScope
{
    /// <summary>
    ///     The execution-context item key under which the mutable scope state is stored.
    /// </summary>
    internal const string ItemKey = "__LiteBus.Audit.Scope";

    /// <inheritdoc />
    public string? TargetId => Find()?.TargetId;

    /// <inheritdoc />
    public string? Reason => Find()?.Reason;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Properties =>
        Find()?.Properties ?? new Dictionary<string, string>(StringComparer.Ordinal);

    /// <inheritdoc />
    public IAuditScope Target(string targetId)
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

        if (executionContext.Items.TryGetValue(ItemKey, out var stored) && stored is AuditScopeState state)
        {
            return state;
        }

        var created = new AuditScopeState();
        executionContext.Items[ItemKey] = created;
        return created;
    }
}
