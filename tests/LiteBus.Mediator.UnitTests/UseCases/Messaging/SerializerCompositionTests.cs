using System.Text.Json;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging;

/// <summary>
///     Covers stating the wire format of persisted messages at the composition root.
/// </summary>
/// <remarks>
///     The default is camel case, and the only way to change it used to be a container registration placed after
///     AddLiteBus. Forgetting that line renamed every property of every durable message without anything failing at
///     startup, which is why the decision belongs on the builder beside UseTimeProvider.
/// </remarks>
public sealed class SerializerCompositionTests
{
    [Fact]
    public async Task The_default_serializer_writes_camel_case()
    {
        await using var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });
        }).BuildServiceProvider();

        var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();
        var payload = await serializer.SerializeAsync(new OrderPlaced(42)).ConfigureAwait(false);

        payload.Should().Contain("\"orderNumber\"");
    }

    [Fact]
    public async Task UseSerializerInstance_states_the_wire_format_at_composition()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General);

        await using var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging =>
                messaging.UseSerializerInstance(new SystemTextJsonMessageSerializer(options)));
        }).BuildServiceProvider();

        var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();
        var payload = await serializer.SerializeAsync(new OrderPlaced(42)).ConfigureAwait(false);

        payload.Should().Contain("\"OrderNumber\"");
    }

    [Fact]
    public async Task UseSerializer_constructs_the_implementation_from_the_container()
    {
        await using var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging => messaging.UseSerializer<PascalCaseMessageSerializer>());
        }).BuildServiceProvider();

        var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();

        serializer.Should().BeOfType<PascalCaseMessageSerializer>();

        var payload = await serializer.SerializeAsync(new OrderPlaced(42)).ConfigureAwait(false);

        payload.Should().Contain("\"OrderNumber\"");
    }

    [Fact]
    public async Task A_stated_serializer_round_trips_a_message()
    {
        await using var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging => messaging.UseSerializer<PascalCaseMessageSerializer>());
        }).BuildServiceProvider();

        var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();
        var payload = await serializer.SerializeAsync(new OrderPlaced(42)).ConfigureAwait(false);
        var restored = await serializer.DeserializeAsync(typeof(OrderPlaced), payload).ConfigureAwait(false);

        restored.Should().BeOfType<OrderPlaced>().Which.OrderNumber.Should().Be(42);
    }

    public sealed record OrderPlaced(int OrderNumber);

    /// <summary>
    ///     A serializer that keeps property names exactly as the CLR declares them.
    /// </summary>
    public sealed class PascalCaseMessageSerializer : IMessageSerializer
    {
        /// <summary>
        ///     The serializer this one delegates to, configured for general (Pascal case) defaults.
        /// </summary>
        private readonly SystemTextJsonMessageSerializer _inner =
            new(new JsonSerializerOptions(JsonSerializerDefaults.General));

        /// <inheritdoc />
        public Task<string> SerializeAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
            where TMessage : notnull
        {
            return _inner.SerializeAsync(message, cancellationToken);
        }

        /// <inheritdoc />
        public Task<object> DeserializeAsync(Type messageType, string payload, CancellationToken cancellationToken = default)
        {
            return _inner.DeserializeAsync(messageType, payload, cancellationToken);
        }
    }
}
