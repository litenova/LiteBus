using LiteBus.Commands.Abstractions;

namespace LiteBus.UnitTests.Data.FakeCommandWithoutResult.Handlers;

public class FakeCommandWithoutResultSyncHandler : ISyncCommandHandler<Messages.FakeCommandWithoutResult>
{
    public void Handle(Messages.FakeCommandWithoutResult message)
    {
        message.ExecutedTypes.Add(typeof(FakeCommandWithoutResultSyncHandler));
    }
}