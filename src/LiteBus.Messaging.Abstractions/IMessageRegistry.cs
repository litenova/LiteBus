using System;
using System.Collections.Generic;
using System.Reflection;

namespace LiteBus.Messaging.Abstractions
{
    public interface IMessageRegistry : IEnumerable<IMessageTypeDescriptor>
    {
        IMessageTypeDescriptor GetDescriptor(Type messageType);
        
        IMessageTypeDescriptor GetDescriptor<TMessage>();

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