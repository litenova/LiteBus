using LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.Shared.Queries;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.FakeGenericQuery.Messages;

// ReSharper disable once UnusedTypeParameter
public sealed class FakeGenericQuery<TPayload> : FakeParentQuery, IQuery<FakeGenericQueryResult>;