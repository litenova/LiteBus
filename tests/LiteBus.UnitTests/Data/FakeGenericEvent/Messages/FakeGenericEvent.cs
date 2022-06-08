using LiteBus.Events.Abstractions;
using LiteBus.UnitTests.Data.Shared.Events;

namespace LiteBus.UnitTests.Data.FakeGenericEvent.Messages;

// ReSharper disable once UnusedTypeParameter
public class FakeGenericEvent<TPayload> : FakeParentEvent, IEvent
{
}