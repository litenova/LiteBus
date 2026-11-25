using System.Text.Json;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Provides the default <see cref="JsonSerializerOptions" /> the message serializer reads and writes payloads with.
/// </summary>
/// <remarks>
///     <para>
///         Registered as the <see cref="IMessageJsonSerializerOptionsProvider" /> LiteBus default when the messaging
///         module builds without an explicit options registration. The options are web defaults, which is camel
///         case, the same format <see cref="SystemTextJsonMessageSerializer" /> used before the provider existed,
///         so stored payloads read before and after.
///     </para>
///     <para>
///         An application states its own wire format with <c>MessageModuleBuilder.UseSerializerOptions</c>, which
///         registers this type over explicit options, or replaces the port with its own provider. The composition
///         model records the replacement.
///     </para>
/// </remarks>
public sealed class DefaultMessageJsonSerializerOptionsProvider : IMessageJsonSerializerOptionsProvider
{
    /// <summary>
    ///     The serializer options the default message serializer uses.
    /// </summary>
    private readonly JsonSerializerOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DefaultMessageJsonSerializerOptionsProvider" /> class.
    /// </summary>
    /// <param name="options">The options to provide, or <see langword="null" /> for web defaults.</param>
    public DefaultMessageJsonSerializerOptionsProvider(JsonSerializerOptions? options = null)
    {
        _options = options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    /// <inheritdoc />
    public JsonSerializerOptions Options => _options;
}
