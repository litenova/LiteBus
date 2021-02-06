using System;
using System.Runtime.Serialization;

namespace Paykan.Queries
{
    [Serializable]
    internal class MultipleQueryHandlerFoundException : Exception
    {
        public MultipleQueryHandlerFoundException(Type queryType) :
            base($"Multiple Query handler found for {queryType}.")
        {
        }
    }
}