using System;

namespace LiteBus.Messaging.Exceptions;

/// <summary>
///     Thrown when a required service type cannot be resolved from the application container.
/// </summary>
[Serializable]
public class NotResolvedException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="NotResolvedException" /> class.
    /// </summary>
    /// <param name="type">The type that could not be resolved.</param>
    public NotResolvedException(Type type) : base($"The type of '{type.Name}' could not be resolved")
    {
    }
}
