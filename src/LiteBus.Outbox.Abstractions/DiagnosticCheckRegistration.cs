using System;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Describes a consumer-owned diagnostic probe collected during outbox composition.
/// </summary>
/// <param name="ImplementationType">The concrete probe type registered in dependency injection.</param>
/// <param name="Name">The probe name reported to operators and health hosts.</param>
internal sealed record DiagnosticCheckRegistration(Type ImplementationType, string Name);
