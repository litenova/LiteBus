using LiteBus.Commands.Abstractions;
using LiteBus.MessageModule.UnitTests.Data.Shared.Commands;

namespace LiteBus.MessageModule.UnitTests.Data.FakeCommand.Messages;

public sealed class FakeCommand : FakeParentCommand, ICommand<FakeCommandResult>;