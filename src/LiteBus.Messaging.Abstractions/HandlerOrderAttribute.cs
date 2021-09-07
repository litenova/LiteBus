using System;

namespace LiteBus.Messaging.Abstractions
{
    [AttributeUsage(AttributeTargets.Class)]
    public class HandlerOrderAttribute : Attribute
    {
        public HandlerOrderAttribute(int order)
        {
            Order = order;
        }

        public int Order { get; }
    }
}