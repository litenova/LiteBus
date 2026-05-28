using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Thrown when a message contract registration conflicts with an existing registration.
/// </summary>
[Serializable]
public sealed class MessageContractAlreadyRegisteredException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageContractAlreadyRegisteredException" /> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public MessageContractAlreadyRegisteredException(string message)
        : base(message)
    {
    }
}