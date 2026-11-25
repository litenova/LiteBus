using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Extensions.Microsoft.Hosting;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Runtime.UnitTests.Hosting;

/// <summary>
///     Covers structural round-trip comparison, the serializer type the check closes over, and malformed
///     composition sections.
/// </summary>
public sealed class RoundTripStructuralComparisonTests
{
    /// <summary>
    ///     Confirms a message with a nested plain-class value passes when every nested value round-trips.
    /// </summary>
    [Fact]
    public void Validate_ContractWithNestedObject_ShouldNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging =>
                messaging.Contracts.Register<NestedOrderEvent>("test.nested"));
            registry.AddEvents(events => events.Register<NestedOrderEvent>());
        });

        var act = () => LiteBusHostValidation.Validate(services);

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Confirms a nested property the serializer drops fails naming the property path.
    /// </summary>
    [Fact]
    public void Validate_ContractWithIgnoredNestedProperty_ShouldNamePropertyPath()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging =>
            {
                messaging.Contracts.Register<HonorificOrderEvent>("test.nested-ignored");
                messaging.UseMessageSampleFactoryInstance(new HonorificSampleFactory());
            });
            registry.AddEvents(events => events.Register<HonorificOrderEvent>());
        });

        var act = () => LiteBusHostValidation.Validate(services);

        var exception = act.Should().Throw<LiteBusHostValidationException>().Which;
        exception.Failures.Should().ContainSingle(failure =>
            failure.Contains(nameof(HonorificOrderEvent), StringComparison.Ordinal)
            && failure.Contains(nameof(HonorificOrderEvent.Customer), StringComparison.Ordinal)
            && failure.Contains(nameof(CustomerHonorific.Honorific), StringComparison.Ordinal));
    }

    /// <summary>
    ///     Confirms a message with populated collection properties passes when every element round-trips.
    /// </summary>
    [Fact]
    public void Validate_ContractWithCollections_ShouldNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging =>
                messaging.Contracts.Register<CollectionOrderEvent>("test.collections"));
            registry.AddEvents(events => events.Register<CollectionOrderEvent>());
        });

        var act = () => LiteBusHostValidation.Validate(services);

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Confirms the round-trip check closes the serializer over the contract message type.
    /// </summary>
    [Fact]
    public void Validate_WithRecordingSerializer_ShouldSerializeAsContractType()
    {
        var recording = new RecordingSerializer();
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging =>
            {
                messaging.Contracts.Register<NestedOrderEvent>("test.nested");
                messaging.UseSerializerInstance(recording);
            });
            registry.AddEvents(events => events.Register<NestedOrderEvent>());
        });

        var act = () => LiteBusHostValidation.Validate(services);

        act.Should().NotThrow();
        recording.SerializedTypes.Should().ContainSingle()
            .Which.Should().Be<NestedOrderEvent>();
    }

    /// <summary>
    ///     Confirms a decisions section with an unrecognized shape fails naming the section.
    /// </summary>
    [Fact]
    public void Validate_WithMalformedDecisionsSection_ShouldNameSection()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });
            registry.Modules.Register(new SectionModule());
        });

        var act = () => LiteBusHostValidation.Validate(services);

        var exception = act.Should().Throw<LiteBusHostValidationException>().Which;
        exception.Failures.Should().ContainSingle(failure =>
            failure.Contains("outbox-domain-events", StringComparison.Ordinal)
            && failure.Contains("unrecognized", StringComparison.Ordinal));
    }

    private sealed record NestedOrderEvent : IEvent
    {
        public string OrderId { get; init; } = string.Empty;

        public CustomerInfo Customer { get; init; } = new();
    }

    private sealed class CustomerInfo
    {
        public string Name { get; set; } = string.Empty;

        public int LoyaltyPoints { get; set; }
    }

    private sealed record HonorificOrderEvent : IEvent
    {
        public string OrderId { get; init; } = string.Empty;

        public CustomerHonorific Customer { get; init; } = new();
    }

    private sealed class CustomerHonorific
    {
        public string Name { get; set; } = string.Empty;

        [JsonIgnore]
        public string Honorific { get; set; } = string.Empty;
    }

    private sealed record CollectionOrderEvent : IEvent
    {
        public List<int> Quantities { get; init; } = [];

        public Dictionary<string, string> Labels { get; init; } = new(StringComparer.Ordinal);
    }

    private sealed class HonorificSampleFactory : IMessageSampleFactory
    {
        public ValueTask<object?> CreateSampleAsync(Type messageType, CancellationToken cancellationToken = default)
        {
            object sample = new HonorificOrderEvent
            {
                OrderId = "order-42",
                Customer = new CustomerHonorific { Name = "Ada", Honorific = "Dr" }
            };

            return new ValueTask<object?>(sample);
        }
    }

    private sealed class RecordingSerializer : IMessageSerializer
    {
        private readonly SystemTextJsonMessageSerializer _inner = new();

        public List<Type> SerializedTypes { get; } = [];

        public async Task<string> SerializeAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
            where TMessage : notnull
        {
            SerializedTypes.Add(typeof(TMessage));

            return await _inner.SerializeAsync(message, cancellationToken).ConfigureAwait(false);
        }

        public Task<object> DeserializeAsync(Type messageType, string payload, CancellationToken cancellationToken = default)
        {
            return _inner.DeserializeAsync(messageType, payload, cancellationToken);
        }
    }

    private sealed class SectionModule : IModule
    {
        public void Build(IModuleConfiguration configuration)
        {
            configuration.DependencyRegistry.RegisterCollection(DependencyDescriptor.ForCollection(
                typeof(ICompositionSectionContributor),
                new MalformedDecisionsSection()));
        }
    }

    private sealed class MalformedDecisionsSection : ICompositionSectionContributor
    {
        public string SectionName => "outbox-domain-events";

        public object? GetData()
        {
            return "not-a-dictionary";
        }
    }
}
