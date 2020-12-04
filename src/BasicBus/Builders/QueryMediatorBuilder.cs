using System;
using BasicBus.Abstractions;
using BasicBus.Internal;

namespace BasicBus.Builders
{
    public class QueryMediatorBuilder
    {
        public IQueryMediator Build(IServiceProvider serviceProvider, 
                                    IMessageRegistry messageRegistry)
        {
            return new QueryMediator(serviceProvider, messageRegistry);
        }
    }
}