using System;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using LiteBus.Queries.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Queries.Extensions.MicrosoftDependencyInjection;

internal class QueryModule : IModule
{
    private readonly Action<QueryModuleBuilder> _builder;

    public QueryModule(Action<QueryModuleBuilder> builder)
    {
        _builder = builder;
    }

    public void Build(ILiteBusModuleConfiguration configuration)
    {
        _builder(new QueryModuleBuilder(configuration.MessageRegistry));

        configuration.Services.TryAddTransient<IQueryMediator, QueryMediator>();
    }
}