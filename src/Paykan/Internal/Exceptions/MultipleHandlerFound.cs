using System;
using System.Runtime.Serialization;

namespace Paykan.Internal.Exceptions
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