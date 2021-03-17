using System;

namespace LightBus.Registry.Internal
{
    internal class HookDescriptor
    {
        public Type MessageType { get; set; }

        public Type HookType { get; set; }
    }
}