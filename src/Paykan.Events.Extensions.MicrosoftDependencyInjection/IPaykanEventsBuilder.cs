using System.Reflection;

namespace Paykan.Events.Extensions.MicrosoftDependencyInjection
{
    public interface IPaykanEventsBuilder
    {
        /// <summary>
        ///     Register message handlers and message hooks from the specified assembly
        /// </summary>
        /// <param name="assembly">The assembly to look for message handler and hooks</param>
        public IPaykanEventsBuilder Register(Assembly assembly);
    }
}