using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Represents an attribute to specify the handling order for a class.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class HandlerOrderAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HandlerOrderAttribute"/> class with the specified order.
    /// </summary>
    /// <param name="order">The handling order.</param>
    public HandlerOrderAttribute(int order)
    {
        Order = order;
    }

    /// <summary>
    /// Gets the handling order specified by the attribute.
    /// </summary>
    public int Order { get; }
}