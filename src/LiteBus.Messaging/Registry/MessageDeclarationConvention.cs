using System;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     One function producing a metadata value from a message type, for messages that state no value of that type
///     themselves.
/// </summary>
/// <param name="ValueType">The metadata value type the convention produces.</param>
/// <param name="Convention">
///     Produces the value for a message type, or <see langword="null" /> when the convention does not apply to it.
/// </param>
/// <remarks>
///     Applied after every other source, so precedence stays definition, then attribute, then default, then
///     convention. A convention observes the message type only: it cannot see other messages or resolve services,
///     because it runs once per message at composition and must stay a pure function of the type.
/// </remarks>
internal sealed record MessageDeclarationConvention(Type ValueType, Func<Type, object?> Convention);
