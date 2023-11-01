using LiteBus.Commands.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases.LogActivity;

public sealed class LogActivityCommand<TPayload> : IAuditableCommand, ICommand
{
    public List<Type> ExecutedTypes { get; } = new();

    public required TPayload Payload { get; init; }
}