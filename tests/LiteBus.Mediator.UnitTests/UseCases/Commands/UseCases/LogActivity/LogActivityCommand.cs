using LiteBus.Commands.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.UseCases.LogActivity;

public sealed class LogActivityCommand<TPayload> : IAuditableCommand, ICommand
{
    public required TPayload Payload { get; init; }

    public List<Type> ExecutedTypes { get; } = new();
}