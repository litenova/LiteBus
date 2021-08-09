using System;

namespace LiteBus.Messaging.Abstractions.Descriptors
{
    public interface IHookDescriptor
    {
        Type HookType { get; }

        int Order { get; }

        Type MessageType { get; }
    }
}