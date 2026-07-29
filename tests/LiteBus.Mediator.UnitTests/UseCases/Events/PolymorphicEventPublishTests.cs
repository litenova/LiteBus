using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Events;

/// <summary>
///     Verifies that publishing an event through a base-typed or interface-typed reference still reaches handlers
///     registered for the concrete runtime type.
/// </summary>
/// <remarks>
///     The base-typed handler subscribes to <see cref="IPolymorphicEvent" /> rather than <see cref="IEvent" /> so that the
///     events assembly scan used by the other event tests does not pick it up as a handler for every event.
/// </remarks>
public sealed class PolymorphicEventPublishTests
{
    [Fact]
    public async Task publishing_through_the_non_generic_overload_invokes_concrete_and_base_typed_handlers()
    {
        await using var serviceProvider = BuildServiceProvider();
        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new SomethingHappened();

        await eventMediator.PublishAsync((IEvent) @event).ConfigureAwait(false);

        @event.ExecutedTypes.Should().BeEquivalentTo([typeof(BaseTypedHandler), typeof(SpecificHandler)]);
    }

    [Fact]
    public async Task publishing_through_the_cancellation_token_extension_invokes_concrete_and_base_typed_handlers()
    {
        await using var serviceProvider = BuildServiceProvider();
        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new SomethingHappened();

        await eventMediator.PublishAsync(@event, CancellationToken.None).ConfigureAwait(false);

        @event.ExecutedTypes.Should().BeEquivalentTo([typeof(BaseTypedHandler), typeof(SpecificHandler)]);
    }

    [Fact]
    public async Task publishing_through_the_tag_extension_invokes_concrete_and_base_typed_handlers()
    {
        await using var serviceProvider = BuildServiceProvider();
        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new SomethingHappened();

        await eventMediator.PublishAsync((IEvent) @event, "untagged-handlers-always-participate").ConfigureAwait(false);

        @event.ExecutedTypes.Should().BeEquivalentTo([typeof(BaseTypedHandler), typeof(SpecificHandler)]);
    }

    [Fact]
    public async Task publishing_through_the_generic_overload_invokes_concrete_and_base_typed_handlers()
    {
        await using var serviceProvider = BuildServiceProvider();
        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new SomethingHappened();

        await eventMediator.PublishAsync(@event, cancellationToken: CancellationToken.None).ConfigureAwait(false);

        @event.ExecutedTypes.Should().BeEquivalentTo([typeof(BaseTypedHandler), typeof(SpecificHandler)]);
    }

    [Fact]
    public async Task publishing_through_the_non_generic_overload_honors_the_cancellation_token()
    {
        await using var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder => builder.Register<TokenCapturingHandler>());
        }).BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new SomethingHappened();
        using var cancellation = new CancellationTokenSource();

        await eventMediator.PublishAsync((IEvent) @event, cancellationToken: cancellation.Token).ConfigureAwait(false);

        @event.ObservedToken.Should().Be(cancellation.Token);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        return new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder =>
            {
                builder.Register<BaseTypedHandler>();
                builder.Register<SpecificHandler>();
            });
        }).BuildServiceProvider();
    }

    private interface IPolymorphicEvent : IEvent
    {
        List<Type> ExecutedTypes { get; }
    }

    private sealed class SomethingHappened : IPolymorphicEvent
    {
        public List<Type> ExecutedTypes { get; } = [];

        public CancellationToken ObservedToken { get; set; }
    }

    private sealed class BaseTypedHandler : IEventHandler<IPolymorphicEvent>
    {
        public Task HandleAsync(IPolymorphicEvent message, CancellationToken cancellationToken = default)
        {
            message.ExecutedTypes.Add(GetType());
            return Task.CompletedTask;
        }
    }

    private sealed class SpecificHandler : IEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened message, CancellationToken cancellationToken = default)
        {
            message.ExecutedTypes.Add(GetType());
            return Task.CompletedTask;
        }
    }

    private sealed class TokenCapturingHandler : IEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened message, CancellationToken cancellationToken = default)
        {
            message.ObservedToken = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
