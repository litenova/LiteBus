using System;

namespace LiteBus.Registry
{
    internal class HookDescriptor
    {
        public Type MessageType { get; set; }

        public Type HookType { get; set; }
    }
}