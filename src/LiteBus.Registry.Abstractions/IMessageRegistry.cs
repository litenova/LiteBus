using System;
using System.Collections.Generic;
using System.Reflection;

namespace LiteBus.Registry.Abstractions
{
    public interface IMessageRegistry : IEnumerable<IMessageDescriptor>
    {
        IMessageDescriptor GetDescriptor(Type messageType);

        /// <summary>
        ///     Registers messages and hooks from the specified assemblies
        /// </summary>
        /// <param name="assemblies">the specified assemblies</param>
        void Register(params Assembly[] assemblies);
        
        /// <summary>
        ///     Registers messages and hooks from the specified types
        /// </summary>
        /// <param name="types">the types to register</param>
        void Register(params Type[] types);
    }
}