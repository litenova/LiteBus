using System;

namespace LiteBus.Commands
{
    [Serializable]
    internal class MultipleCommandHandlerFoundException : Exception
    {
        public MultipleCommandHandlerFoundException(Type commandType)
            : base($"Multiple command handler found for {commandType}.")
        {
        }
    }
}