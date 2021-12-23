using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.Shared.Queries;

namespace LiteBus.UnitTests.Data.FakeQuery.Messages;

public class FakeQuery : FakeParentQuery, IQuery<FakeQueryResult>
{
}