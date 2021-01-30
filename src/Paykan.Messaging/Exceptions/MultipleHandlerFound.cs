using System;

namespace Paykan.Messaging.Exceptions
{
    [Serializable]
    public class MultipleHandlerFoundException : Exception
    {
        public MultipleHandlerFoundException(string messageName) :
            base($"Multiple handler found for {messageName}.")
        {
        }
    }
}