using System;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Describes a consumer-owned diagnostic probe collected by <see cref="InboxModuleBuilder" />.
/// </summary>
/// <param name="ImplementationType">The concrete probe type registered in dependency injection.</param>
/// <param name="Name">The probe name reported to operators and health hosts.</param>
internal sealed record DiagnosticCheckRegistration(Type ImplementationType, string Name);