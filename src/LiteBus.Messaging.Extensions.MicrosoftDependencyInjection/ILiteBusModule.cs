using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection
{
    public interface ILiteBusModule
    {
        void Build(ILiteBusModuleConfiguration configuration);
    }
}