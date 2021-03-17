using System;

namespace LiteBus.Queries
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