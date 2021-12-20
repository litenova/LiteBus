using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

public interface ILiteBusModuleConfiguration
{
    IServiceCollection Services { get; }

    IMessageRegistry MessageRegistry { get; }
}