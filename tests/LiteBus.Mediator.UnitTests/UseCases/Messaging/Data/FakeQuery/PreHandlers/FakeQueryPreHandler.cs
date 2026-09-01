using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.FakeQuery.PreStageHandlers;

public sealed class FakeQueryPreHandler : IQueryPreHandler<Messages.FakeQuery>
{
    public Task PreHandleAsync(Messages.FakeQuery message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeQueryPreHandler));
        return Task.CompletedTask;
    }
}