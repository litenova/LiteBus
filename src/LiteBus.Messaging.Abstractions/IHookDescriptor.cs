using System;

namespace LiteBus.Messaging.Abstractions
{
    public interface IHookDescriptor
    {
        Type HookType { get; }
        
        int Order { get; }
        
        Type MessageType { get; }
    }
}