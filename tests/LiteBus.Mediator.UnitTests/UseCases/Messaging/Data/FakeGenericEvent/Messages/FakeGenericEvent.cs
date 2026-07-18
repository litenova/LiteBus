using LiteBus.Events.Abstractions;
using LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.Shared.Events;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.FakeGenericEvent.Messages;

// ReSharper disable once UnusedTypeParameter
public sealed class FakeGenericEvent<TPayload> : FakeParentEvent, IEvent;