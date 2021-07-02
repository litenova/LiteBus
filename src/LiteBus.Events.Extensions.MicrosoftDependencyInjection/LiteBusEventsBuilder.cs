using System;
using System.Collections.Generic;
using System.Reflection;

namespace LiteBus.Events.Extensions.MicrosoftDependencyInjection
{
    internal class LiteBusEventsBuilder : ILiteBusEventsBuilder
    {
        public LiteBusEventsBuilder()
        {
            Assemblies = new HashSet<Assembly>();
            Types = new HashSet<Type>();
        }

        public HashSet<Assembly> Assemblies { get; }
        public HashSet<Type> Types { get; }

        public ILiteBusEventsBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }

        public ILiteBusEventsBuilder Register(Type type)
        {
            Types.Add(type);

            return this;
        }
    }
}