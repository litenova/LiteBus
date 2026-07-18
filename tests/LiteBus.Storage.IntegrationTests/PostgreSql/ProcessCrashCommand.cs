using LiteBus.Commands.Abstractions;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

internal sealed record ProcessCrashCommand : ICommand
{
    public required Guid WorkId { get; init; }
}
