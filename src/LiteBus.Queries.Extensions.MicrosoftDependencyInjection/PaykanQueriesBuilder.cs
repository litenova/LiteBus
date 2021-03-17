using System.Collections.Generic;
using System.Reflection;

namespace LiteBus.Queries.Extensions.MicrosoftDependencyInjection
{
    internal class LiteBusQueriesBuilder : ILiteBusQueriesBuilder
    {
        public LiteBusQueriesBuilder()
        {
            Assemblies = new HashSet<Assembly>();
        }

        public HashSet<Assembly> Assemblies { get; }

        public ILiteBusQueriesBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }
    }
}