using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.Shared.Queries;

namespace LiteBus.UnitTests.Data.FakeGenericQuery.Messages;

public class FakeGenericQuery<TPayload> : FakeParentQuery, IQuery<FakeGenericQueryResult>
{
}