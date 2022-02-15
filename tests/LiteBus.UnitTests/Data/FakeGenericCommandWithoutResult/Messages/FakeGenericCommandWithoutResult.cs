using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.Shared.Commands;

namespace LiteBus.UnitTests.Data.FakeGenericCommandWithoutResult.Messages;

public class FakeGenericCommandWithoutResult<TPayload> : FakeParentCommand, ICommand
{
}