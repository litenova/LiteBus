using System.Reflection;

namespace LiteBus.Queries.Extensions.MicrosoftDependencyInjection
{
    public interface ILiteBusQueriesBuilder
    {
        /// <summary>
        ///     Register message handlers and message hooks from the specified assembly
        /// </summary>
        /// <param name="assembly">The assembly to look for message handler and hooks</param>
        public ILiteBusQueriesBuilder Register(Assembly assembly);
    }
}