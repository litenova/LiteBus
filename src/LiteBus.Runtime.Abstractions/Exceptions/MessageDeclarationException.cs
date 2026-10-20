using System;

namespace LiteBus.Runtime.Abstractions.Exceptions;

/// <summary>
///     Thrown when message metadata is contradictory: two definitions declaring the same value for one message, a definition that declares nothing, or an attribute whose annotation disagrees with the value it produces.
/// </summary>
/// <remarks>
///     Catch this to separate a declaration mistake from the rest of composition. A team enforcing its own
///     conventions through <c>ValidateComposition</c> throws from that callback instead, because a convention is
///     the application's rule rather than a contradiction in the model.
/// </remarks>
public sealed class MessageDeclarationException : LiteBusConfigurationException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageDeclarationException" /> class.
    /// </summary>
    /// <param name="message">The configuration error message.</param>
    public MessageDeclarationException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageDeclarationException" /> class.
    /// </summary>
    /// <param name="message">The configuration error message.</param>
    /// <param name="innerException">The exception that caused this configuration failure.</param>
    public MessageDeclarationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
