using System.Reflection;

namespace Paykan.Messaging.Extensions.MicrosoftDependencyInjection
{
    public interface IPaykanMessagingBuilder
    {
        /// <summary>
        ///     Register message handlers and message hooks from the specified assembly
        /// </summary>
        /// <param name="assembly">The assembly to look for message handler and hooks</param>
        public IPaykanMessagingBuilder Register(Assembly assembly);
    }
}