using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     States that a message deliberately declares no value of one metadata type, and records why.
/// </summary>
/// <param name="DeclarationType">The metadata value type the message is exempt from declaring.</param>
/// <param name="Rationale">The recorded reason the message is exempt.</param>
/// <remarks>
///     An exemption is a decision, not an omission, and the difference only exists if the reason is written down. A
///     message with no declaration is indistinguishable from one nobody considered, which is exactly what
///     <c>RequireDeclaration&lt;TValue&gt;</c> exists to prevent.
/// </remarks>
public sealed record DeclarationExemption(Type DeclarationType, string Rationale);
