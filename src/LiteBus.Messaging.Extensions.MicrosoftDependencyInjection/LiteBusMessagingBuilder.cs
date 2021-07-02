using System;
using System.Collections.Generic;
using System.Reflection;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection
{
    internal class LiteBusMessagingBuilder : ILiteBusMessagingBuilder
    {
        public LiteBusMessagingBuilder()
        {
            Assemblies = new HashSet<Assembly>();
            Types = new HashSet<Type>();
        }

        public HashSet<Type> Types { get; }
        public HashSet<Assembly> Assemblies { get; }

        public ILiteBusMessagingBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }

        public ILiteBusMessagingBuilder Register(Type type)
        {
            Types.Add(type);

            return this;
        }
    }
}