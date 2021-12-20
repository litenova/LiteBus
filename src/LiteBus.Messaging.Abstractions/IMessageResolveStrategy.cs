using System;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Abstractions;

public interface IMessageResolveStrategy
{
    IMessageDescriptor Find(Type messageType, IMessageRegistry messageRegistry);
}