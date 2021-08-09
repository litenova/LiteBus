using System;

namespace LiteBus.Messaging.Abstractions
{
    [AttributeUsage(AttributeTargets.Class)]
    public class HookOrderAttribute : Attribute
    {
        public HookOrderAttribute(int order)
        {
            Order = order;
        }

        public int Order { get; }
    }
}