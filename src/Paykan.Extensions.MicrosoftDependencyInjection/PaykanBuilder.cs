using System.Collections.Generic;
using System.Reflection;

namespace Paykan.Extensions.MicrosoftDependencyInjection
{
    internal class PaykanBuilder : IPaykanBuilder
    {
        public PaykanBuilder()
        {
            Assemblies = new HashSet<Assembly>();
        }

        public HashSet<Assembly> Assemblies { get; }

        public IPaykanBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }
    }
}