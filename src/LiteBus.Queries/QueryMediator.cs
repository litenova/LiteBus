using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Workflows.Discovery;
using LiteBus.Messaging.Workflows.Execution;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries;

public class QueryMediator : IQueryMediator
{
    private readonly IMessageMediator _messageMediator;

    public QueryMediator(IMessageMediator messageMediator)
    {
        _messageMediator = messageMediator;
    }

    public Task<TQueryResult> QueryAsync<TQueryResult>(IQuery<TQueryResult> query,
                                                       CancellationToken cancellationToken = default)
    {
        var executionWorkflow =
            new SingleAsyncHandlerExecutionWorkflow<IQuery<TQueryResult>, TQueryResult>(cancellationToken);

        var findStrategy = new ActualTypeOrFirstAssignableTypeDiscoveryWorkflow();

        return _messageMediator.Mediate(query, findStrategy, executionWorkflow);
    }

    public IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(IStreamQuery<TQueryResult> query,
                                                                    CancellationToken cancellationToken = default)
    {
        var executionWorkflow =
            new SingleStreamHandlerExecutionWorkflow<IStreamQuery<TQueryResult>, TQueryResult>(cancellationToken);

        var findStrategy = new ActualTypeOrFirstAssignableTypeDiscoveryWorkflow();

        return _messageMediator.Mediate(query, findStrategy, executionWorkflow);
    }
}