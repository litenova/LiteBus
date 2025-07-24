using LiteBus.MessageModule.UnitTests.Data.Shared.Queries;
using LiteBus.Queries.Abstractions;

namespace LiteBus.MessageModule.UnitTests.Data.FakeQuery.Messages;

public sealed class FakeQuery : FakeParentQuery, IQuery<FakeQueryResult>;