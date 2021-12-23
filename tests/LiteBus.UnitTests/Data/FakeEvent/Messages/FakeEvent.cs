using LiteBus.Events.Abstractions;
using LiteBus.UnitTests.Data.Shared.Events;

namespace LiteBus.UnitTests.Data.FakeEvent.Messages;

public class FakeEvent : FakeParentEvent, IEvent
{
}