using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection
{
    public interface IModule
    {
        void Build(IServiceCollection services, IMessageRegistry messageRegistry);
    }
}