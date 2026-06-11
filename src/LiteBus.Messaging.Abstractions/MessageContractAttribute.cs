using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Marks a message type with the stable contract name and version used by inbox and outbox storage.
/// </summary>
/// <remarks>
///     Apply this attribute when you want contract metadata to be discovered from the CLR type. Call
///     <see cref="IContractWriter.AddFromAssembly" /> or
///     <see cref="IContractWriterExtensions.RegisterFromAssembly(IContractWriter, System.Reflection.Assembly)" />
///     during module configuration, or register explicitly with <see cref="IContractWriter.Register{TMessage}" />.
///     The attribute is read at runtime through assembly scanning, not only by compile-time analyzers.
///     When both an attribute and explicit registration are present, the name and version must match.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
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