using System.Reflection;

namespace BasicBus.Abstractions
{
    public interface IMessageRegistryBuilder
    {
        IMessageRegistryBuilder RegisterHandlers(Assembly assembly);

        IMessageRegistry Build();
    }
}