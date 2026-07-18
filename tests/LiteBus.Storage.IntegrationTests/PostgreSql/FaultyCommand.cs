using LiteBus.Commands.Abstractions;
using LiteBus.Storage.PostgreSql;
using LiteBus.Inbox;
using LiteBus.Outbox;
using LiteBus.Messaging;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

internal sealed record FaultyCommand : ICommand;