namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection
{
    public interface ILiteBusBuilder
    {
        ILiteBusBuilder AddModule(IModule module);
    }
}