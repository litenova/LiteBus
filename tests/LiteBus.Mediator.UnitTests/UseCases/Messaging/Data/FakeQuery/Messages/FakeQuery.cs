using LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.Shared.Queries;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.FakeQuery.Messages;

public sealed class FakeQuery : FakeParentQuery, IQuery<FakeQueryResult>;