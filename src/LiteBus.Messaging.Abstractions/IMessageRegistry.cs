using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Abstractions;

public interface IMessageRegistry : IReadOnlyCollection<IMessageDescriptor>
{
    void Register(Type type);
}