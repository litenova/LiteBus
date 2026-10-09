using System;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Thrown when a message declares idempotency but its key cannot be resolved: the selector produced a blank key,
///     or the message declares a context-supplied key and no stage supplied one.
/// </summary>
/// <remarks>
///     <para>
///         A blank key is a declaration mistake, not a message failure: every message with a blank key shares one key
///         space, so the first would answer all the others. A missing context-supplied key is a pipeline wiring
///         mistake, because no guard stored one before the idempotency shortcut ran. Both are reported rather than
///         defaulted, so the declaration cannot appear to work while doing nothing.
///     </para>
///     <para>
///         This is a configuration error rather than a message failure, so it propagates unchanged instead of reaching
///         error handlers. See <see cref="MediationExceptionFilters.IsRecoverableMediationException" />.
///     </para>
/// </remarks>
public sealed class IdempotencyConfigurationException : LiteBusConfigurationException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="IdempotencyConfigurationException" /> class.
    /// </summary>
    /// <param name="message">The configuration error message.</param>
    public IdempotencyConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="IdempotencyConfigurationException" /> class.
    /// </summary>
    /// <param name="message">The configuration error message.</param>
    /// <param name="innerException">The exception that caused this configuration failure.</param>
    public IdempotencyConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
