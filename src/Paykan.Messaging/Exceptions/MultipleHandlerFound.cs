using System;

namespace Paykan.Messaging.Exceptions
{
    [Serializable]
    public class MultipleMessageHandlerFoundException : Exception
    {
        public MultipleMessageHandlerFoundException(string messageName) :
            base($"Multiple handler found for {messageName}.")
        {
        }
    }
}