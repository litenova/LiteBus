using System;

namespace LiteBus.Analyzers;

/// <summary>
///     Marks a message type with the stable contract name and version used by inbox and outbox storage.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class MessageContractAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageContractAttribute" /> class.
    /// </summary>
    /// <param name="name">The stable contract name persisted with the message payload.</param>
    /// <param name="version">The positive contract version persisted with the message payload.</param>
    public MessageContractAttribute(string name, int version = 1)
    {
        Name = name;
        Version = version;
    }

    /// <summary>
    ///     Gets the stable contract name persisted with the message payload.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the positive contract version persisted with the message payload.
    /// </summary>
    public int Version { get; }
}
