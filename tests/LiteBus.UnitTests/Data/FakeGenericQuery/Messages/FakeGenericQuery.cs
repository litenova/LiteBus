using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.Shared.Queries;

namespace LiteBus.UnitTests.Data.FakeGenericQuery.Messages;

// ReSharper disable once UnusedTypeParameter
public class FakeGenericQuery<TPayload> : FakeParentQuery, IQuery<FakeGenericQueryResult>
{
}