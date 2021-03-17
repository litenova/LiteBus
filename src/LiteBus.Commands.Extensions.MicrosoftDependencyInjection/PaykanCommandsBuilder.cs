using System.Collections.Generic;
using System.Reflection;

namespace LiteBus.Commands.Extensions.MicrosoftDependencyInjection
{
    internal class LiteBusCommandsBuilder : ILiteBusCommandsBuilder
    {
        public LiteBusCommandsBuilder()
        {
            Assemblies = new HashSet<Assembly>();
        }

        public HashSet<Assembly> Assemblies { get; }

        public ILiteBusCommandsBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }
    }
}