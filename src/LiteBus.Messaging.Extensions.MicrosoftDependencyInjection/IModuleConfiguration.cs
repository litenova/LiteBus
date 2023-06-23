using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

public interface IModuleConfiguration
{
    IServiceCollection Services { get; }

    IMessageRegistry MessageRegistry { get; }
}