using System;

namespace LiteBus.Messaging.Abstractions;

public interface IMessageResolveStrategy
{
    IMessageDescriptor Find(Type messageType, IMessageRegistry messageRegistry);
}