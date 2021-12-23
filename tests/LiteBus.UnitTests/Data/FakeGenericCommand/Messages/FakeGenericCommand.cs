using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.Shared.Commands;

namespace LiteBus.UnitTests.Data.FakeGenericCommand.Messages;

public class FakeGenericCommand<TPayload> : FakeParentCommand, ICommand<FakeGenericCommandResult>
{
}