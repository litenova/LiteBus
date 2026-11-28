using System.Text.Json;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Tenancy;

namespace LiteBus.Mediator.UnitTests.Registry.Composition;

/// <summary>
///     A composition section contributor standing in for any feature-owned section.
/// </summary>
internal sealed class TestSectionContributor : ICompositionSectionContributor
{
    /// <inheritdoc />
    public string SectionName => "test";

    /// <inheritdoc />
    public object? GetData()
    {
        return new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["jobs"] = 2,
            ["paused"] = false,
        };
    }
}

/// <summary>
///     A second composition section contributor naming the same section as
///     <see cref="TestSectionContributor" />.
/// </summary>
internal sealed class SecondTestSectionContributor : ICompositionSectionContributor
{
    /// <inheritdoc />
    public string SectionName => "test";

    /// <inheritdoc />
    public object? GetData()
    {
        return new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["jobs"] = 5,
        };
    }
}

/// <summary>
///     A message serializer standing in for an application wire format.
/// </summary>
internal sealed class TestSerializer : IMessageSerializer
{
    /// <inheritdoc />
    public Task<string> SerializeAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        return Task.FromResult(JsonSerializer.Serialize(message));
    }

    /// <inheritdoc />
    public Task<object> DeserializeAsync(Type messageType, string payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(payload);

        return Task.FromResult(
            JsonSerializer.Deserialize(payload, messageType)
            ?? throw new InvalidOperationException($"The payload deserializes to no '{messageType.Name}'."));
    }
}

/// <summary>
///     A tenant metadata provider standing in for an application tenant store.
/// </summary>
internal sealed class CustomMetadataProvider : ITenantMetadataProvider
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, TenantMetadata>> GetManyAsync(
        IReadOnlyList<string> tenantIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantIds);

        Dictionary<string, TenantMetadata> result = new(StringComparer.Ordinal);

        foreach (var tenantId in tenantIds)
        {
            Dictionary<string, string> attributes = [];
            result[tenantId] = new TenantMetadata(tenantId, null, null, attributes);
        }

        return new ValueTask<IReadOnlyDictionary<string, TenantMetadata>>(result);
    }
}
