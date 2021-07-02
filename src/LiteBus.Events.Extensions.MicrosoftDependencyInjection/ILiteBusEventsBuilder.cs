using System;
using System.Reflection;

namespace LiteBus.Events.Extensions.MicrosoftDependencyInjection
{
    public interface ILiteBusEventsBuilder
    {
        /// <summary>
        ///     Register message handlers and message hooks from the specified assembly
        /// </summary>
        /// <param name="assembly">The assembly to look for message handler and hooks</param>
        public ILiteBusEventsBuilder Register(Assembly assembly);
        
        /// <summary>
        ///     Register the specified type
        /// </summary>
        /// <param name="type">The specified type</param>
        public ILiteBusEventsBuilder Register(Type type);
    }
}