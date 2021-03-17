using System;

namespace LightBus.Registry.Internal
{
    [Serializable]
    internal class MessageNotRegisteredException : Exception
    {
        public MessageNotRegisteredException(Type messageType) :
            base($"The message type '{messageType.Name}' is not registered")
        {
        }
    }
}