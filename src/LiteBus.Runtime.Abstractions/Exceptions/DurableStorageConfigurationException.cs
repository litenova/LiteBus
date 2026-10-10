using System;

namespace LiteBus.Runtime.Abstractions.Exceptions;

/// <summary>
///     Thrown when durable messaging is enabled without the storage, dispatch, or processor registration it needs, or against a schema it cannot use.
/// </summary>
/// <remarks>
///     The inbox and outbox each need a store, a dispatcher, and, to process, a host loop. Enabling one without
///     the others composes cleanly and then does nothing, so the gap is reported here instead. Schema drift is in
///     the same category because the fix is the same kind of decision: change what is registered or migrate what
///     is stored.
/// </remarks>
public sealed class DurableStorageConfigurationException : LiteBusConfigurationException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="DurableStorageConfigurationException" /> class.
    /// </summary>
    /// <param name="message">The configuration error message.</param>
    public DurableStorageConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="DurableStorageConfigurationException" /> class.
    /// </summary>
    /// <param name="message">The configuration error message.</param>
    /// <param name="innerException">The exception that caused this configuration failure.</param>
    public DurableStorageConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
