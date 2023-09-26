using System;

namespace LiteBus.Messaging.Abstractions;

public interface IDescriptor
{
    Type MessageType { get; }
}