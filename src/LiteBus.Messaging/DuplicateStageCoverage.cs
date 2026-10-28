using System;

namespace LiteBus.Messaging;

/// <summary>
///     One handler type that covers one message's pipeline stage twice, once directly and once through a base type.
/// </summary>
/// <param name="HandlerType">The open generic handler definition or closed handler type covering both sets.</param>
/// <param name="MessageType">The registered message covered twice.</param>
/// <param name="DirectContractType">The contract closed over the message itself.</param>
/// <param name="IndirectContractType">The contract closed over a base type of the message.</param>
/// <remarks>
///     Both entries run: the pipeline executes the indirect and direct sets together, indirect first for pre-stage and
///     error entries and direct first for post entries, with completion ordered by priority across both. This record
///     names the double run; it never removes one, because a cross-cutting handler plus a message-specific one is the
///     designed composition.
/// </remarks>
public sealed record DuplicateStageCoverage(Type HandlerType, Type MessageType, Type DirectContractType, Type IndirectContractType);
