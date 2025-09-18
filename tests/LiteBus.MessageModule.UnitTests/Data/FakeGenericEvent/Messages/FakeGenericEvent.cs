using LiteBus.Events.Abstractions;
using LiteBus.MessageModule.UnitTests.Data.Shared.Events;

namespace LiteBus.MessageModule.UnitTests.Data.FakeGenericEvent.Messages;

// ReSharper disable once UnusedTypeParameter
public sealed class FakeGenericEvent<TPayload> : FakeParentEvent, IEvent;