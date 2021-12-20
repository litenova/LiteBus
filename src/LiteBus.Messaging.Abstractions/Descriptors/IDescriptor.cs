using System;

namespace LiteBus.Messaging.Abstractions.Descriptors;

public interface IDescriptor
{
    Type MessageType { get; }
}