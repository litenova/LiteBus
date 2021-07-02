using System;
using System.Collections.Generic;
using System.Reflection;

namespace LiteBus.Extensions.MicrosoftDependencyInjection
{
    internal class LiteBusBuilder : ILiteBusBuilder
    {
        public LiteBusBuilder()
        {
            Assemblies = new HashSet<Assembly>();
            Types = new HashSet<Type>();
        }

        public HashSet<Type> Types { get; }
        public HashSet<Assembly> Assemblies { get; }

        public ILiteBusBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }
        
        public ILiteBusBuilder Register(Type type)
        {
            Types.Add(type);

            return this;
        }
    }
}