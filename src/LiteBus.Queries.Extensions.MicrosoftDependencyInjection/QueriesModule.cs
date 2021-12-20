using System;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using LiteBus.Queries.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Queries.Extensions.MicrosoftDependencyInjection;

internal class QueriesModule : ILiteBusModule
{
    private readonly Action<LiteBusQueryBuilder> _builder;

    public QueriesModule(Action<LiteBusQueryBuilder> builder)
    {
        _builder = builder;
    }

    public void Build(ILiteBusModuleConfiguration configuration)
    {
        _builder(new LiteBusQueryBuilder(configuration.MessageRegistry));

        configuration.Services.TryAddTransient<IQueryMediator, QueryMediator>();
    }
}