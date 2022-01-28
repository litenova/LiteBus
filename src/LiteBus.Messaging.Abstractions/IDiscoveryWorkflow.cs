using System;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a workflow of finding a registered message from message message registry 
/// </summary>
public interface IDiscoveryWorkflow
{
    /// <summary>
    ///     Discovers a message from message registry
    /// </summary>
    /// <param name="messageRegistry">The message registry containing registered messages</param>
    /// <param name="messageType">The given message type</param>
    /// <returns></returns>
    IMessageDescriptor Discover(IMessageRegistry messageRegistry, Type messageType);
}