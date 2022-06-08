using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.Shared.Commands;

namespace LiteBus.UnitTests.Data.FakeGenericCommand.Messages;

// ReSharper disable once UnusedTypeParameter
public class FakeGenericCommand<TPayload> : FakeParentCommand, ICommand<FakeGenericCommandResult>
{
}