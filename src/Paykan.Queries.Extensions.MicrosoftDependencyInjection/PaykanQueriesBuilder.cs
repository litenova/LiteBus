using System.Collections.Generic;
using System.Reflection;

namespace Paykan.Queries.Extensions.MicrosoftDependencyInjection
{
    internal class PaykanQueriesBuilder : IPaykanQueriesBuilder
    {
        public PaykanQueriesBuilder()
        {
            Assemblies = new HashSet<Assembly>();
        }

        public HashSet<Assembly> Assemblies { get; }

        public IPaykanQueriesBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }
    }
}