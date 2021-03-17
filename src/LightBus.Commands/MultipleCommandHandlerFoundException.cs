using System;

namespace LightBus.Commands
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