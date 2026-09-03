using System;

namespace LiteBus.Runtime.Abstractions.Exceptions;

/// <summary>
///     Thrown when auditing is enabled but cannot write: no audit trail is registered, or the feature is configured with no axis to audit.
/// </summary>
/// <remarks>
///     Kept apart from <c>AuditReasonMissingException</c>, which is a data problem in one mediation rather
///     than a composition mistake. An application catching configuration faults at startup should not also catch a
///     handler that forgot to supply a reason.
/// </remarks>
public sealed class AuditConfigurationException : LiteBusConfigurationException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditConfigurationException" /> class.
    /// </summary>
    /// <param name="message">The configuration error message.</param>
    public AuditConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditConfigurationException" /> class.
    /// </summary>
    /// <param name="message">The configuration error message.</param>
    /// <param name="innerException">The exception that caused this configuration failure.</param>
    public AuditConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
