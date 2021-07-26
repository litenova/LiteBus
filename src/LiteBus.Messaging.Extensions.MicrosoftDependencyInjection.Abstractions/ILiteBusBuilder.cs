using System;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection.Abstractions
{
    public interface ILiteBusBuilder
    {
        ILiteBusBuilder AddModule(IModule module);
    }
}