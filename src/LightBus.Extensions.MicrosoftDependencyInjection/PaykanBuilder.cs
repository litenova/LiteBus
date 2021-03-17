using System.Collections.Generic;
using System.Reflection;

namespace LightBus.Extensions.MicrosoftDependencyInjection
{
    internal class LightBusBuilder : ILightBusBuilder
    {
        public LightBusBuilder()
        {
            Assemblies = new HashSet<Assembly>();
        }

        public HashSet<Assembly> Assemblies { get; }

        public ILightBusBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }
    }
}