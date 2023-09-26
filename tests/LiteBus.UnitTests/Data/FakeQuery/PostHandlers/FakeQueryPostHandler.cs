using System.Threading;
using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.FakeQuery.Messages;

namespace LiteBus.UnitTests.Data.FakeQuery.PostHandlers;

public sealed class FakeQueryPostHandler : IQueryPostHandler<FakeQuery.Messages.FakeQuery, FakeQueryResult>
{
    public Task PostHandleAsync(Messages.FakeQuery message, FakeQueryResult messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeQueryPostHandler));
        return Task.CompletedTask;
    }
}