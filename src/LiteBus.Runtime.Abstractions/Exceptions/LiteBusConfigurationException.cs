using System;

namespace LiteBus.Runtime.Abstractions.Exceptions;

/// <summary>
///     Thrown when LiteBus module registration or runtime composition is invalid.
/// </summary>
/// <remarks>
///     <para>
///         This is the category, and it stays catchable as one: almost every case is a startup failure that should
///         end the process, and a host that wants to report any composition mistake catches this.
///     </para>
///     <para>
///         Derived types name what kind of mistake it was, so a host that needs to tell a handler wiring error apart
///         from a missing durable store can. It used to be one type for duplicate modules, dependency cycles, missing
///         storage, a missing audit trail, refusal mapper conflicts, metadata conflicts and untyped shortcut misuse,
///         which meant nothing could be caught selectively. Throw a derived type from new code; the base remains for
///         a failure that fits no category.
///     </para>
/// </remarks>
public class LiteBusConfigurationException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusConfigurationException" /> class.
    /// </summary>
    /// <param name="message">The configuration error message.</param>
    public LiteBusConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusConfigurationException" /> class.
    /// </summary>
    /// <param name="message">The configuration error message.</param>
    /// <param name="innerException">The exception that caused this configuration failure.</param>
    public LiteBusConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}