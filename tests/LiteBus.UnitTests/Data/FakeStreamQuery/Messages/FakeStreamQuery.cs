using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.Shared.Queries;

namespace LiteBus.UnitTests.Data.FakeStreamQuery.Messages;

public class FakeStreamQuery : FakeParentQuery, IStreamQuery<FakeStreamQueryResult>
{
}