using System;

namespace LiteBus.Messaging.Abstractions.Metadata;

public interface IDescriptor
{
    Type MessageType { get; }
}