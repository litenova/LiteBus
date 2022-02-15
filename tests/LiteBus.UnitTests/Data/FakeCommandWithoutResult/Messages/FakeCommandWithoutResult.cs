using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.Shared.Commands;

namespace LiteBus.UnitTests.Data.FakeCommandWithoutResult.Messages;

public class FakeCommandWithoutResult : FakeParentCommand, ICommand
{
}