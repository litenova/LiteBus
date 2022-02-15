using System.Collections.Generic;
using System.Threading;
using LiteBus.Messaging.Workflows.Utilities;
using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericStreamQuery.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericStreamQuery.Handlers;

public class
    FakeGenericStreamQueryHandler<TPayload> : IStreamQueryHandler<FakeGenericStreamQuery<TPayload>,
        FakeGenericStreamQueryResult>
{
    public IAsyncEnumerable<FakeGenericStreamQueryResult> HandleAsync(FakeGenericStreamQuery<TPayload> message,
                                                                      CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeGenericStreamQueryHandler<TPayload>));
        return AsyncEnumerable.Empty<FakeGenericStreamQueryResult>();
    }
}