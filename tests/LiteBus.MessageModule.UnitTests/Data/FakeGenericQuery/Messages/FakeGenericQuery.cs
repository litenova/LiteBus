using LiteBus.MessageModule.UnitTests.Data.Shared.Queries;
using LiteBus.Queries.Abstractions;

namespace LiteBus.MessageModule.UnitTests.Data.FakeGenericQuery.Messages;

// ReSharper disable once UnusedTypeParameter
public sealed class FakeGenericQuery<TPayload> : FakeParentQuery, IQuery<FakeGenericQueryResult>;