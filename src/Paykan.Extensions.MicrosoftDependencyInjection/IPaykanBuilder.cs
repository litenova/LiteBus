using System.Reflection;

namespace Paykan.Extensions.MicrosoftDependencyInjection
{
    public interface IPaykanBuilder
    {
        /// <summary>
        /// Register message handlers and message hooks from the specified assembly
        /// </summary>
        /// <param name="assembly">The assembly to look for message handler and hooks</param>
        public IPaykanBuilder Register(Assembly assembly);
    }
}