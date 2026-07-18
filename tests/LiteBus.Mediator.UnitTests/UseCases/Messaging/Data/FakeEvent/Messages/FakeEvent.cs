using LiteBus.Events.Abstractions;
using LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.Shared.Events;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.FakeEvent.Messages;

public sealed class FakeEvent : FakeParentEvent, IEvent;