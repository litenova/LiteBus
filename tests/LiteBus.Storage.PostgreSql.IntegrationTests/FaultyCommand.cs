using LiteBus.Commands.Abstractions;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

internal sealed record FaultyCommand : ICommand;
