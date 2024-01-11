using LiteBus.Queries.Abstractions;

namespace LiteBus.UnitTests.Data.FakeQuery.PreHandlers;

public sealed class FakeQueryPreHandler : IQueryPreHandler<FakeQuery.Messages.FakeQuery>
{
    public Task PreHandleAsync(Messages.FakeQuery message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeQueryPreHandler));
        return Task.CompletedTask;
    }
}