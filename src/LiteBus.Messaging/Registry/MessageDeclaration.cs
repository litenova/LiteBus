using System;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     One metadata value contributed by a message definition, resolved during registration.
/// </summary>
/// <param name="MessageType">The normalized message type the declaration describes.</param>
/// <param name="KeyType">The metadata key type, which is the declared value type.</param>
/// <param name="Value">The metadata value written to the message descriptor.</param>
/// <param name="DefinitionType">The definition type that declared the value, used in diagnostics.</param>
internal sealed record MessageDeclaration(Type MessageType, Type KeyType, object Value, Type DefinitionType);
