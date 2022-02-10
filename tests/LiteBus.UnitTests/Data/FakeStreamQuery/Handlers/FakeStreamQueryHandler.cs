using System.Collections.Generic;
using System.Threading;
using LiteBus.Messaging.Workflows.Utilities;
using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.FakeStreamQuery.Messages;

namespace LiteBus.UnitTests.Data.FakeStreamQuery.Handlers;

public class FakeStreamQueryHandler : IStreamQueryHandler<Messages.FakeStreamQuery, FakeStreamQueryResult>
{
    public IAsyncEnumerable<FakeStreamQueryResult> HandleAsync(Messages.FakeStreamQuery message,
                                                               CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeStreamQueryHandler));
        return AsyncEnumerable.Empty<FakeStreamQueryResult>();
    }
}