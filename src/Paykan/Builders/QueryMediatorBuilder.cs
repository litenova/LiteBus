using System;
using Paykan.Abstractions;
using Paykan.Internal;

namespace Paykan.Builders
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