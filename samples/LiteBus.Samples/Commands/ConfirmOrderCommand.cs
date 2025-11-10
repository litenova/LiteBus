using LiteBus.Commands.Abstractions;

namespace LiteBus.Samples.Commands;

public sealed record ConfirmOrderCommand(Guid OrderId) : ICommand;