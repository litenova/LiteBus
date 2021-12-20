using System.Collections.Generic;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection
{
    public interface ILiteBusConfiguration
    {
        ILiteBusConfiguration AddModule(ILiteBusModule liteBusModule);
    }
}