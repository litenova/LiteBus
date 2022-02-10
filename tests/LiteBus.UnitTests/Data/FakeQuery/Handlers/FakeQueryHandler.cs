using System.Threading;
using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.FakeQuery.Messages;

namespace LiteBus.UnitTests.Data.FakeQuery.Handlers;

public class FakeQueryHandler : IQueryHandler<Messages.FakeQuery, FakeQueryResult>
{
    public Task<FakeQueryResult> HandleAsync(Messages.FakeQuery message,
                                             CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeQueryHandler));
        return Task.FromResult(new FakeQueryResult(message.CorrelationId));
    }
}