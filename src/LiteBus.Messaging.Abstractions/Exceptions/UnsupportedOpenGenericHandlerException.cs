using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Thrown when an open generic handler has a shape LiteBus cannot close for concrete messages.
/// </summary>
[Serializable]
public sealed class UnsupportedOpenGenericHandlerException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="UnsupportedOpenGenericHandlerException" /> class.
    /// </summary>
    /// <param name="handlerType">The open generic handler type.</param>
    /// <param name="genericParameterCount">The number of generic parameters declared by the handler type.</param>
    public UnsupportedOpenGenericHandlerException(Type handlerType, int genericParameterCount)
        : base($"Open generic handler type '{handlerType.FullName ?? handlerType.Name}' declares {genericParameterCount} generic parameters. LiteBus supports open generic handlers with exactly one generic parameter.")
    {
        HandlerType = handlerType;
        GenericParameterCount = genericParameterCount;
    }

    /// <summary>
    ///     Gets the unsupported handler type.
    /// </summary>
    public Type HandlerType { get; }

    /// <summary>
    ///     Gets the number of generic parameters declared by the handler type.
    /// </summary>
    public int GenericParameterCount { get; }
}