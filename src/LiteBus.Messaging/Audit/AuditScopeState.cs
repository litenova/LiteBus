using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Audit;

/// <summary>
///     The mutable audit detail pushed by a handler during one mediation.
/// </summary>
internal sealed class AuditScopeState
{
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
    public Dictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);
}
