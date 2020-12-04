using System.Reflection;

namespace BasicBus.Abstractions
{
    public interface ICommandHandlerRegistryBuilder
    {
        ICommandHandlerRegistryBuilder RegisterHandlers(Assembly assembly,
                                                        bool registerGlobalInterceptors = true,
                                                        bool registerIndividualInterceptors = true);

        ICommandHandlerRegistry Build();
    }
}