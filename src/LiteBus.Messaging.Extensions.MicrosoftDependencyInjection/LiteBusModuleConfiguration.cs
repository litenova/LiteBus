using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

internal class LiteBusModuleConfiguration : ILiteBusModuleConfiguration
{
    public LiteBusModuleConfiguration(IServiceCollection services, IMessageRegistry messageRegistry)
    {
        Services = services;
        MessageRegistry = messageRegistry;
    }

    public IServiceCollection Services { get; }

    public IMessageRegistry MessageRegistry { get; }
}