using LiteBus.Commands.Abstractions;
using LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.Shared.Commands;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.FakeGenericCommand.Messages;

// ReSharper disable once UnusedTypeParameter
public sealed class FakeGenericCommand<TPayload> : FakeParentCommand, ICommand<FakeGenericCommandResult>;