using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.Shared.Queries;

namespace LiteBus.UnitTests.Data.FakeGenericStreamQuery.Messages;

public class FakeGenericStreamQuery<TPayload> : FakeParentQuery, IStreamQuery<FakeGenericStreamQueryResult>
{
}