using System.Collections.Generic;
using System.Reflection;

namespace LiteBus.Extensions.MicrosoftDependencyInjection
{
    internal class LiteBusBuilder : ILiteBusBuilder
    {
        public LiteBusBuilder()
        {
            Assemblies = new HashSet<Assembly>();
        }

        public HashSet<Assembly> Assemblies { get; }

        public ILiteBusBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }
    }
}