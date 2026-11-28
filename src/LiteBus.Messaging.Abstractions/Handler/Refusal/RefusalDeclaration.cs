namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     One refusal code a message declares it can produce, independent of any transport.
/// </summary>
/// <param name="Code">The exact refusal code, or a prefix pattern ending in <c>.*</c>.</param>
/// <param name="Kind">The refusal kind used for default mappings.</param>
/// <param name="Title">The stable short title.</param>
/// <param name="Retryable">Whether retrying the same request can succeed.</param>
/// <remarks>
///     <para>
///         Declared through <see cref="IMessageDeclarations.Refusals" />, so a definition class, an attribute, a
///         family default, or a convention states the codes beside the message that produces them. At composition,
///         every message's declarations are harvested into the refusal catalog, and transports answer them without
///         a hand-registered map.
///     </para>
/// </remarks>
public sealed record RefusalDeclaration(string Code, RefusalKind Kind, string Title, bool Retryable = false);
