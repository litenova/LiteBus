using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

public interface IMessageRegistry : IReadOnlyCollection<IMessageDescriptor>
{
    void Register(Type type);
}