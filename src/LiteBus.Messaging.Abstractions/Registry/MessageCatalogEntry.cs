using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     One registered message and the metadata it declares.
/// </summary>
/// <param name="MessageType">The registered message type.</param>
/// <param name="Metadata">Every declaration resolved for the message, keyed by value type.</param>
/// <remarks>
///     The pair a composition check reads. It carries the resolved metadata rather than the declaration sources, so a
///     check sees what the pipeline will see, including a value inherited from a base type or a marker interface.
/// </remarks>
public sealed record MessageCatalogEntry(Type MessageType, IMessageMetadata Metadata);
