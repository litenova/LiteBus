using System;
using System.Reflection;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection
{
    public interface ILiteBusMessagingBuilder
    {
        /// <summary>
        ///     Register message handlers and message hooks from the specified assembly
        /// </summary>
        /// <param name="assembly">The assembly to look for message handler and hooks</param>
        public ILiteBusMessagingBuilder Register(Assembly assembly);
        
        /// <summary>
        ///     Register the specified type
        /// </summary>
        /// <param name="type">The specified type</param>
        public ILiteBusMessagingBuilder Register(Type type);
    }
}