using System;
using System.Collections.Generic;
using System.Reflection;

namespace LiteBus.Commands.Extensions.MicrosoftDependencyInjection
{
    internal class LiteBusCommandsBuilder : ILiteBusCommandsBuilder
    {
        public LiteBusCommandsBuilder()
        {
            Assemblies = new HashSet<Assembly>();
            Types = new HashSet<Type>();
        }

        public HashSet<Assembly> Assemblies { get; }

        public HashSet<Type> Types { get; }

        public ILiteBusCommandsBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }

        public ILiteBusCommandsBuilder Register(Type type)
        {
            Types.Add(type);

            return this;
        }
    }
}