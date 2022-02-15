using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeCommandWithoutResult.PreHandlers;

public class FakeCommandWithoutResultSyncPreHandler : ISyncCommandPreHandler<FakeCommandWithoutResult.Messages.FakeCommandWithoutResult>
{
    public void Handle(IHandleContext<Messages.FakeCommandWithoutResult> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeCommandWithoutResultSyncPreHandler));
    }
}