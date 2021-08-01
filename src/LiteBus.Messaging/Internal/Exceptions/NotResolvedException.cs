using System;
using System.Runtime.Serialization;

namespace LiteBus.Messaging.Internal.Exceptions
{
    [Serializable]
    public class NotResolvedException : Exception
    {
        public NotResolvedException(Type type) : base($"The type of '{type.Name}' could not be resolved")
        {
        }
    }
}