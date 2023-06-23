namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

public interface IModuleRegistry
{
    IModuleRegistry Register(IModule module);
}