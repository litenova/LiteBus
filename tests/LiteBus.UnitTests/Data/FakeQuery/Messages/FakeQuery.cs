using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.Shared.Queries;

namespace LiteBus.UnitTests.Data.FakeQuery.Messages;

public sealed class FakeQuery : FakeParentQuery, IQuery<FakeQueryResult>
{
}