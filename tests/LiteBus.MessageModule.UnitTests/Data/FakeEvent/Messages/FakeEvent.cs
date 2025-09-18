using LiteBus.Events.Abstractions;
using LiteBus.MessageModule.UnitTests.Data.Shared.Events;

namespace LiteBus.MessageModule.UnitTests.Data.FakeEvent.Messages;

public sealed class FakeEvent : FakeParentEvent, IEvent;