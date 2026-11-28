using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Extensions.Microsoft.Hosting;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Runtime.UnitTests.Hosting;

/// <summary>
///     Covers durable host validation checks and the shared serializer options source.
/// </summary>
public sealed class DurableCompositionCheckTests
{
    /// <summary>
    ///     Confirms a composition with one round-trippable contract passes validation.
    /// </summary>
    [Fact]
    public void Validate_ContractWithRoundTrippableMessage_ShouldNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging =>
                messaging.Contracts.Register<SimpleEvent>("test.simple"));
            registry.AddEvents(events => events.Register<SimpleEvent>());
        });

        var act = () => LiteBusHostValidation.Validate(services);

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Confirms a contract whose serializer drops a property fails naming the type and the property.
    /// </summary>
    [Fact]
    public void Validate_ContractWithIgnoredProperty_ShouldNameTypeAndProperty()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging =>
                messaging.Contracts.Register<IgnoredPropertyEvent>("test.ignored-property"));
            registry.AddEvents(events => events.Register<IgnoredPropertyEvent>());
        });

        var act = () => LiteBusHostValidation.Validate(services);

        var exception = act.Should().Throw<LiteBusHostValidationException>().Which;
        exception.Failures.Should().ContainSingle(failure =>
            failure.Contains(nameof(IgnoredPropertyEvent), StringComparison.Ordinal)
            && failure.Contains(nameof(IgnoredPropertyEvent.InternalNote), StringComparison.Ordinal));
    }

    /// <summary>
    ///     Confirms the round-trip check can be switched off while registration checks still run.
    /// </summary>
    [Fact]
    public void Validate_WithRoundTripCheckSwitchedOff_ShouldPassDespiteIgnoredProperty()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging =>
                messaging.Contracts.Register<IgnoredPropertyEvent>("test.ignored-property"));
            registry.AddEvents(events => events.Register<IgnoredPropertyEvent>());
        });

        var act = () => LiteBusHostValidation.Validate(
            services,
            new LiteBusHostValidationOptions { ValidateContractRoundTrips = false });

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Confirms a sample factory that cannot sample a type fails naming the type and the port.
    /// </summary>
    [Fact]
    public void Validate_WithNullSample_ShouldReportMissingSample()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging =>
            {
                messaging.Contracts.Register<SimpleEvent>("test.simple");
                messaging.UseMessageSampleFactoryInstance(NullSampleFactory.Instance);
            });
            registry.AddEvents(events => events.Register<SimpleEvent>());
        });

        var act = () => LiteBusHostValidation.Validate(services);

        var exception = act.Should().Throw<LiteBusHostValidationException>().Which;
        exception.Failures.Should().ContainSingle(failure =>
            failure.Contains(nameof(SimpleEvent), StringComparison.Ordinal)
            && failure.Contains(nameof(IMessageSampleFactory), StringComparison.Ordinal));
    }

    /// <summary>
    ///     Confirms a custom sample factory is consulted for round-trip samples.
    /// </summary>
    [Fact]
    public void Validate_WithCustomSampleFactory_ShouldUseIt()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging =>
            {
                messaging.Contracts.Register<IgnoredPropertyEvent>("test.ignored-property");
                messaging.UseMessageSampleFactoryInstance(new FixedSampleFactory());
            });
            registry.AddEvents(events => events.Register<IgnoredPropertyEvent>());
        });

        var act = () => LiteBusHostValidation.Validate(services);

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Confirms a staged event with a contract and a handler passes validation.
    /// </summary>
    [Fact]
    public void Validate_StagedEventWithContractAndHandler_ShouldNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });
            registry.AddEvents(events =>
            {
                events.Register<StagedDomainEvent>();
                events.Register<StagedDomainEventHandler>();
            });
            registry.AddOutbox(outbox =>
            {
                outbox.Contracts.Register<StagedDomainEvent>("test.staged");
                outbox.UseInMemoryStorage();
                outbox.StageCollectedEvents();
                outbox.StageDomainEvent<StagedDomainEvent>();
            });
        });

        var act = () => LiteBusHostValidation.Validate(services);

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Confirms a staged event with no subscribed handler fails naming the event type.
    /// </summary>
    [Fact]
    public void Validate_StagedEventWithoutHandler_ShouldNameEvent()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });
            registry.AddEvents(events => events.Register<StagedDomainEvent>());
            registry.AddOutbox(outbox =>
            {
                outbox.Contracts.Register<StagedDomainEvent>("test.staged");
                outbox.UseInMemoryStorage();
                outbox.StageCollectedEvents();
                outbox.StageDomainEvent<StagedDomainEvent>();
            });
        });

        var act = () => LiteBusHostValidation.Validate(services);

        var exception = act.Should().Throw<LiteBusHostValidationException>().Which;
        exception.Failures.Should().ContainSingle(failure =>
            failure.Contains(nameof(StagedDomainEvent), StringComparison.Ordinal)
            && failure.Contains("no handler", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Confirms a staged event with no registered contract fails naming the event type.
    /// </summary>
    [Fact]
    public void Validate_StagedEventWithoutContract_ShouldNameEvent()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });
            registry.AddEvents(events =>
            {
                events.Register<StagedDomainEvent>();
                events.Register<StagedDomainEventHandler>();
            });
            registry.AddOutbox(outbox =>
            {
                outbox.UseInMemoryStorage();
                outbox.StageCollectedEvents();
                outbox.StageDomainEvent<StagedDomainEvent>();
            });
        });

        var act = () => LiteBusHostValidation.Validate(services);

        var exception = act.Should().Throw<LiteBusHostValidationException>().Which;
        exception.Failures.Should().ContainSingle(failure =>
            failure.Contains(nameof(StagedDomainEvent), StringComparison.Ordinal)
            && failure.Contains("no message contract", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Confirms the event-consistency check can be switched off.
    /// </summary>
    [Fact]
    public void Validate_WithEventCheckSwitchedOff_ShouldPassDespiteMissingHandler()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });
            registry.AddEvents(events => events.Register<StagedDomainEvent>());
            registry.AddOutbox(outbox =>
            {
                outbox.Contracts.Register<StagedDomainEvent>("test.staged");
                outbox.UseInMemoryStorage();
                outbox.StageCollectedEvents();
                outbox.StageDomainEvent<StagedDomainEvent>();
            });
        });

        var act = () => LiteBusHostValidation.Validate(
            services,
            new LiteBusHostValidationOptions { ValidateEventConsistency = false });

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Confirms options registered once flow into the default serializer payloads.
    /// </summary>
    [Fact]
    public async Task Serializer_WithCustomOptions_ShouldSerializeWithThoseOptions()
    {
        using var provider = new ServiceCollection()
            .AddLiteBus(registry => registry.AddMessaging(messaging =>
                messaging.UseSerializerOptions(new JsonSerializerOptions())))
            .BuildServiceProvider();

        var serializer = provider.GetRequiredService<IMessageSerializer>();
        var payload = await serializer.SerializeAsync(new SimpleEvent("order-42")).ConfigureAwait(false);

        payload.Should().Contain("\"OrderId\":\"order-42\"");
    }

    /// <summary>
    ///     Confirms the default composition reports both new ports as defaults.
    /// </summary>
    [Fact]
    public void CompositionModel_DefaultComposition_ShouldReportSerializerPortsAsDefaults()
    {
        using var provider = new ServiceCollection()
            .AddLiteBus(registry => registry.AddMessaging(_ =>
            {
            }))
            .BuildServiceProvider();

        var model = provider.GetRequiredService<ICompositionModel>();

        model.Ports.Should().ContainSingle(port =>
                port.PortType == typeof(IMessageJsonSerializerOptionsProvider))
            .Which.Should().Be(new CompositionPortEntry(
                typeof(IMessageJsonSerializerOptionsProvider),
                typeof(DefaultMessageJsonSerializerOptionsProvider),
                true));
        model.Ports.Should().ContainSingle(port =>
                port.PortType == typeof(IMessageSampleFactory))
            .Which.Should().Be(new CompositionPortEntry(
                typeof(IMessageSampleFactory),
                typeof(DefaultMessageSampleFactory),
                true));
    }

    /// <summary>
    ///     Confirms custom options are reported as a replaced port.
    /// </summary>
    [Fact]
    public void CompositionModel_WithCustomOptions_ShouldReportProviderAsReplaced()
    {
        using var provider = new ServiceCollection()
            .AddLiteBus(registry => registry.AddMessaging(messaging =>
                messaging.UseSerializerOptions(new JsonSerializerOptions())))
            .BuildServiceProvider();

        var model = provider.GetRequiredService<ICompositionModel>();

        model.Ports.Should().ContainSingle(port =>
                port.PortType == typeof(IMessageJsonSerializerOptionsProvider))
            .Which.Should().Be(new CompositionPortEntry(
                typeof(IMessageJsonSerializerOptionsProvider),
                typeof(FixedMessageJsonSerializerOptionsProvider),
                false));
    }

    /// <summary>
    ///     Confirms a custom serializer together with custom options fails composition.
    /// </summary>
    [Fact]
    public void AddMessaging_WithCustomSerializerAndCustomOptions_ShouldFailComposition()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(registry => registry.AddMessaging(messaging => messaging
                .UseSerializerInstance(new SystemTextJsonMessageSerializer())
                .UseSerializerOptions(new JsonSerializerOptions())))
            .BuildServiceProvider();

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*custom message serializer*custom serializer options*");
    }

    private sealed record SimpleEvent(string OrderId) : IEvent;

    private sealed record IgnoredPropertyEvent : IEvent
    {
        public string OrderId { get; init; } = string.Empty;

        [JsonIgnore]
        public string InternalNote { get; init; } = string.Empty;
    }

    private sealed record StagedDomainEvent(Guid OrderId) : IEvent;

    private sealed class StagedDomainEventHandler : IEventHandler<StagedDomainEvent>
    {
        public Task HandleAsync(StagedDomainEvent message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NullSampleFactory : IMessageSampleFactory
    {
        public static NullSampleFactory Instance { get; } = new();

        public ValueTask<object?> CreateSampleAsync(Type messageType, CancellationToken cancellationToken = default)
        {
            return new ValueTask<object?>((object?) null);
        }
    }

    private sealed class FixedSampleFactory : IMessageSampleFactory
    {
        private readonly DefaultMessageSampleFactory _defaults = new();

        public async ValueTask<object?> CreateSampleAsync(Type messageType, CancellationToken cancellationToken = default)
        {
            if (messageType == typeof(IgnoredPropertyEvent))
            {
                return new IgnoredPropertyEvent { OrderId = "order-42", InternalNote = string.Empty };
            }

            return await _defaults.CreateSampleAsync(messageType, cancellationToken).ConfigureAwait(false);
        }
    }
}
