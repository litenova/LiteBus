using System.Text.Json;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Provides the <see cref="JsonSerializerOptions" /> the default message serializer reads and writes payloads with.
/// </summary>
/// <remarks>
///     <para>
///         This is the single source for the message wire format. Outbox, inbox, sagas, and idempotency all
///         serialize envelope payloads through <see cref="IMessageSerializer" />, and the default serializer reads
///         its options from here, so one registration states the format for every durable feature instead of one
///         per axis.
///     </para>
///     <para>
///         The default is web defaults, which is camel case. Replace this port through
///         <c>MessageModuleBuilder.UseSerializerOptions</c> or <c>UseSerializerOptionsProvider</c> to state a
///         different format at the composition root. A custom <see cref="IMessageSerializer" /> that does not read
///         this port decides its own format; composing one together with custom options fails composition, because
///         two payload formats with nothing saying which applies would otherwise stand.
///     </para>
/// </remarks>
public interface IMessageJsonSerializerOptionsProvider
{
    /// <summary>
    ///     Gets the serializer options the default message serializer uses.
    /// </summary>
    /// <value>The shared options instance. Callers must not mutate it after the host starts serializing.</value>
    JsonSerializerOptions Options { get; }
}
