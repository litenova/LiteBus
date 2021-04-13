using System;
using System.Collections.Generic;
using System.Reflection;

namespace LiteBus.Queries.Extensions.MicrosoftDependencyInjection
{
    internal class LiteBusQueriesBuilder : ILiteBusQueriesBuilder
    {
        public LiteBusQueriesBuilder()
        {
            Assemblies = new HashSet<Assembly>();
            Types = new HashSet<Type>();
        }

        public HashSet<Type> Types { get; set; }

        public HashSet<Assembly> Assemblies { get; }

        public ILiteBusQueriesBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }

        public ILiteBusQueriesBuilder Register(Type type)
        {
            Types.Add(type);

            return this;
        }
    }
}