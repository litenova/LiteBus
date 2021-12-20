namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

public interface ILiteBusModule
{
    void Build(ILiteBusModuleConfiguration configuration);
}