using System.Reflection;

namespace Paykan.Abstractions
{
    public interface IMessageRegistryBuilder
    {
        IMessageRegistryBuilder RegisterHandlers(Assembly assembly);

        IMessageRegistry Build();
    }
}