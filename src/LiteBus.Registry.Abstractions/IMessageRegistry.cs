using System;
using System.Collections.Generic;
using System.Reflection;

namespace LiteBus.Registry.Abstractions
{
    public interface IMessageRegistry : IEnumerable<IMessageDescriptor>
    {
        IMessageDescriptor GetDescriptor(Type messageType);

        /// <summary>
        ///     Registers messages from the specified assemblies
        /// </summary>
        /// <param name="assemblies">the specified assemblies</param>
        void Register(params Assembly[] assemblies);
        
        void Register(params Type[] types);
    }
}