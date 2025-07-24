using LiteBus.MessageModule.UnitTests.Data.FakeQuery.Messages;
using LiteBus.Queries.Abstractions;

namespace LiteBus.MessageModule.UnitTests.Data.FakeQuery.PostHandlers;

public sealed class FakeQueryPostHandler : IQueryPostHandler<Messages.FakeQuery, FakeQueryResult>
{
    public Task PostHandleAsync(Messages.FakeQuery message, FakeQueryResult? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeQueryPostHandler));
        return Task.CompletedTask;
    }
}