using LiteBus.Commands.Abstractions;
using LiteBus.MessageModule.UnitTests.Data.Shared.Commands;

namespace LiteBus.MessageModule.UnitTests.Data.FakeGenericCommand.Messages;

// ReSharper disable once UnusedTypeParameter
public sealed class FakeGenericCommand<TPayload> : FakeParentCommand, ICommand<FakeGenericCommandResult>;