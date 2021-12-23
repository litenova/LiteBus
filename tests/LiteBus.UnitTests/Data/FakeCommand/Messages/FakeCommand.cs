using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.Shared.Commands;

namespace LiteBus.UnitTests.Data.FakeCommand.Messages;

public class FakeCommand : FakeParentCommand, ICommand<FakeCommandResult>
{
}