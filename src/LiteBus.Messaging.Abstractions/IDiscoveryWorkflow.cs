using System;
using LiteBus.Messaging.Abstractions.Metadata;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents the message discovery from message registry workflow 
/// </summary>
public interface IDiscoveryWorkflow
{
    /// <summary>
    ///     Discovers the corresponding <see cref="IMessageDescriptor"/> of a given message type from message registry
    /// </summary>
    /// <param name="messageRegistry">The message registry containing registered messages</param>
    /// <param name="messageType">The given message type</param>
    /// <returns></returns>
    IMessageDescriptor Discover(IMessageRegistry messageRegistry, Type messageType);
}