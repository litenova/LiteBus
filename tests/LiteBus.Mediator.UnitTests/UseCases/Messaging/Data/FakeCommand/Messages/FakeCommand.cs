using LiteBus.Commands.Abstractions;
using LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.Shared.Commands;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.FakeCommand.Messages;

public sealed class FakeCommand : FakeParentCommand, ICommand<FakeCommandResult>;