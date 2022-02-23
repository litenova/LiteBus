using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Workflows.Discovery;
using LiteBus.Messaging.Workflows.Execution.Handle;
using LiteBus.Messaging.Workflows.Resolution.Lazy;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries;

public class QueryMediator : IQueryMediator
{
    private readonly IMediator _mediator;

    public QueryMediator(IMediator mediator)
    {
        _mediator = mediator;
    }

    public Task<TQueryResult> QueryAsync<TQueryResult>(IQuery<TQueryResult> query,
                                                       CancellationToken cancellationToken = default)
    {
        var executionWorkflow =
            new SingleAsyncHandlerExecutionWorkflow<IQuery<TQueryResult>, TQueryResult>(cancellationToken);

        var findStrategy = new ActualTypeOrFirstAssignableTypeDiscoveryWorkflow();

        return _mediator.Mediate(query, findStrategy, new LazyResolutionWorkflow(), executionWorkflow);
    }

    public TQueryResult Query<TQueryResult>(IQuery<TQueryResult> query)
    {
        var executionWorkflow =
            new SingleSyncHandlerExecutionWorkflow<IQuery<TQueryResult>, TQueryResult>();

        var findStrategy = new ActualTypeOrFirstAssignableTypeDiscoveryWorkflow();

        return _mediator.Mediate(query, findStrategy, new LazyResolutionWorkflow(), executionWorkflow);
    }

    public IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(IStreamQuery<TQueryResult> query,
                                                                    CancellationToken cancellationToken = default)
    {
        var executionWorkflow =
            new SingleStreamHandlerExecutionWorkflow<IStreamQuery<TQueryResult>, TQueryResult>(cancellationToken);

        var findStrategy = new ActualTypeOrFirstAssignableTypeDiscoveryWorkflow();

        return _mediator.Mediate(query, findStrategy, new LazyResolutionWorkflow(), executionWorkflow);
    }
}