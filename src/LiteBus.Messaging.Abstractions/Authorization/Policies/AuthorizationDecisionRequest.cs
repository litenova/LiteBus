using System;

namespace LiteBus.Messaging.Abstractions.Authorization;

/// <summary>
///     The input one authorization decision reads: who acts, what action on what message, which resource it targets,
///     and when the mediation started.
/// </summary>
/// <param name="Identity">Who the mediation runs for: the calling principal and the tenant the work belongs to.</param>
/// <param name="Action">The action being decided, in the application's own vocabulary.</param>
/// <param name="Message">The message being mediated.</param>
/// <param name="MessageType">The runtime type of the message being mediated.</param>
/// <param name="ResourceKind">The kind of resource the action targets, or <see langword="null" /> when the action targets no named resource.</param>
/// <param name="ResourceId">The identifier of the targeted resource, or <see langword="null" /> when the action targets no named resource.</param>
/// <param name="HandledAt">The instant the mediation started, read once from the configured time provider.</param>
/// <remarks>
///     Built once per evaluation by the decision policy from the evaluation context and the action and resource the
///     policy names, so each decision observes the same subject, message, and instant the policy does. The
///     cancellation token stays a method parameter on the decision point and never becomes a member of this record,
///     so one request serves decisions with different lifetimes.
/// </remarks>
public sealed record AuthorizationDecisionRequest(
    ExecutionIdentity Identity,
    string Action,
    object Message,
    Type MessageType,
    string? ResourceKind,
    string? ResourceId,
    DateTimeOffset HandledAt);
