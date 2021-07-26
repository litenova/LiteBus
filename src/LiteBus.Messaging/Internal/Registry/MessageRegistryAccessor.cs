using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Registry
{
    /// <summary>
    ///     Provides access to the singleton instance of <see cref="IMessageRegistry" />
    /// </summary>
    public static class MessageRegistryAccessor
    {
        static MessageRegistryAccessor()
        {
            MessageRegistry = new MessageRegistry();
        }

        /// <summary>
        ///     Singleton instance of <see cref="IMessageRegistry" />
        /// </summary>
        public static IMessageRegistry MessageRegistry { get; }
    }
}