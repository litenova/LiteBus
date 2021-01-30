using System;
using Paykan.Queries.Abstraction;
using Paykan.Registry.Abstractions;

namespace Paykan.Queries
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