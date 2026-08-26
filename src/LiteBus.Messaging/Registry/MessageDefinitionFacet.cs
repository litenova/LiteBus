using System;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     One metadata value contributed by a message definition, resolved during registration.
/// </summary>
/// <param name="MessageType">The normalized message type the facet describes.</param>
/// <param name="KeyType">The metadata key type, which is the facet's value type.</param>
/// <param name="Value">The metadata value written to the message descriptor.</param>
/// <param name="DefinitionType">The definition type that declared the facet, used in diagnostics.</param>
internal sealed record MessageDefinitionFacet(Type MessageType, Type KeyType, object Value, Type DefinitionType);
