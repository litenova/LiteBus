using System;
using System.Collections.Concurrent;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Audit;

/// <summary>
///     The mutable audit detail pushed by a handler during one mediation.
/// </summary>
internal sealed class AuditScopeState
{
    /// <summary>
    ///     Gets or sets the actor the handler attributed the action to.
    /// </summary>
    public AuditActor? Actor { get; set; }

    /// <summary>
    ///     Gets or sets the identifier of the resource the message acted on.
    /// </summary>
    public string? TargetId { get; set; }

    /// <summary>
    ///     Gets or sets the reason the action was taken.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    ///     Gets the additional non-identifying properties attached by the handler.
    /// </summary>
    /// <remarks>
    ///     Event handlers running in parallel over one mediation write through <see cref="IAuditScope.WithProperty" />
    ///     concurrently, so this is a concurrent dictionary rather than a plain one. Two handlers racing to set the
    ///     same name still leave whichever landed last.
    /// </remarks>
    public ConcurrentDictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);
}
