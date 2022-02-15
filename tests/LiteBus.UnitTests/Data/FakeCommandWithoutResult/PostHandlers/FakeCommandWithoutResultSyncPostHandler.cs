using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeCommandWithoutResult.PostHandlers;

public class FakeCommandWithoutResultSyncPostHandler : ISyncCommandPostHandler<FakeCommandWithoutResult.Messages.FakeCommandWithoutResult>
{
    public void Handle(IHandleContext<Messages.FakeCommandWithoutResult> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeCommandWithoutResultSyncPostHandler));
    }
}