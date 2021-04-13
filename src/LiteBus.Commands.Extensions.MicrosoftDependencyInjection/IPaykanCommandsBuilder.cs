using System;
using System.Reflection;

namespace LiteBus.Commands.Extensions.MicrosoftDependencyInjection
{
    public interface ILiteBusCommandsBuilder
    {
        /// <summary>
        ///     Register message handlers and message hooks from the specified assembly
        /// </summary>
        /// <param name="assembly">The assembly to look for message handler and hooks</param>
        public ILiteBusCommandsBuilder Register(Assembly assembly);
        
        /// <summary>
        ///     Register the specified type
        /// </summary>
        /// <param name="type">The specified type</param>
        public ILiteBusCommandsBuilder Register(Type type);
    }
}