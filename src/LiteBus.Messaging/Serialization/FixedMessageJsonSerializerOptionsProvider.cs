using System;
using System.Text.Json;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Provides fixed JSON serializer options for message payloads.
/// </summary>
/// <remarks>
///     The provider <see cref="MessageModuleBuilder.UseSerializerOptions" /> registers: one options instance shared
///     by every durable feature for the life of the process.
/// </remarks>
public sealed class FixedMessageJsonSerializerOptionsProvider : IMessageJsonSerializerOptionsProvider
{
    /// <summary>
    ///     The shared options instance.
    /// </summary>
    private readonly JsonSerializerOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FixedMessageJsonSerializerOptionsProvider" /> class.
    /// </summary>
    /// <param name="options">The shared JSON serializer options used for message payloads.</param>
    public FixedMessageJsonSerializerOptionsProvider(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public JsonSerializerOptions Options => _options;
}
