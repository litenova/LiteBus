using System;

namespace LiteBus.Queries
{
    [Serializable]
    internal class MultipleQueryHandlerFoundException : Exception
    {
        public MultipleQueryHandlerFoundException(Type queryType) :
            base($"Multiple query handler found for '{queryType}'.")
        {
        }
    }
}