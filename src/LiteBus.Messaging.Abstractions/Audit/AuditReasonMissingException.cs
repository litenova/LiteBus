using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Thrown when an audited action declares that a reason is required and the handler supplied none.
/// </summary>
/// <remarks>
///     <para>
///         This is a data problem in one mediation, not a composition error, which is why it does not derive from
///         <c>LiteBusConfigurationException</c>. It used to, and that made an application catching configuration
///         faults at startup catch a per-message fault at runtime instead.
///     </para>
///     <para>
///         It is raised from the completion stage at <see cref="HandlerPriorities.Observability" />, which is before
///         the commit at <see cref="HandlerPriorities.UnitOfWork" />, so the work the handler did is rolled back. That
///         is the point of <see cref="AuditedDeclaration.ReasonRequired" />: an action whose justification has to be
///         recorded is an action that must not stand without one. Set the flag only where that trade is the one you
///         want, and call <see cref="IAuditScope.WithReason" /> on every path the handler can return through.
///     </para>
/// </remarks>
public sealed class AuditReasonMissingException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditReasonMissingException" /> class.
    /// </summary>
    /// <param name="action">The audit action that declares a reason is required.</param>
    /// <param name="messageType">The message whose handler supplied no reason.</param>
    /// <exception cref="ArgumentNullException"><paramref name="messageType" /> is <see langword="null" />.</exception>
    public AuditReasonMissingException(string action, Type messageType)
        : base(BuildMessage(action, messageType))
    {
        Action = action;
        MessageType = messageType;
    }

    /// <summary>
    ///     Gets the audit action that declares a reason is required.
    /// </summary>
    public string Action { get; }

    /// <summary>
    ///     Gets the message whose handler supplied no reason.
    /// </summary>
    public Type MessageType { get; }

    /// <summary>
    ///     Builds the exception message, naming the action, the message, and both ways out.
    /// </summary>
    /// <param name="action">The audit action that declares a reason is required.</param>
    /// <param name="messageType">The message whose handler supplied no reason.</param>
    /// <returns>The exception message.</returns>
    private static string BuildMessage(string action, Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        return $"The action '{action}' declares that a reason is required, but the handler for '{messageType.Name}' "
               + "supplied none, so the mediation is failed rather than recorded without its justification. Call "
               + "IAuditScope.WithReason before the handler returns, or drop ReasonRequired from the declaration.";
    }
}
