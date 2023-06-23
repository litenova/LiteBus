namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

public interface IModule
{
    void Build(IModuleConfiguration configuration);
}