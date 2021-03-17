using System.Collections.Generic;
using System.Reflection;

namespace LightBus.Commands.Extensions.MicrosoftDependencyInjection
{
    internal class LightBusCommandsBuilder : ILightBusCommandsBuilder
    {
        public LightBusCommandsBuilder()
        {
            Assemblies = new HashSet<Assembly>();
        }

        public HashSet<Assembly> Assemblies { get; }

        public ILightBusCommandsBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }
    }
}