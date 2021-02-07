using System.Reflection;

namespace Paykan.Queries.Extensions.MicrosoftDependencyInjection
{
    public interface IPaykanQueriesBuilder
    {
        /// <summary>
        ///     Register message handlers and message hooks from the specified assembly
        /// </summary>
        /// <param name="assembly">The assembly to look for message handler and hooks</param>
        public IPaykanQueriesBuilder Register(Assembly assembly);
    }
}