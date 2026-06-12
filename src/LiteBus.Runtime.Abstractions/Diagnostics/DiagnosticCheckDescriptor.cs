using System;
using System.Diagnostics;

namespace LiteBus.Runtime.Abstractions.Diagnostics;

/// <summary>
///     Describes a diagnostic probe registered by a LiteBus module for host execution.
/// </summary>
/// <param name="ImplementationType">The concrete type that implements <see cref="IDiagnosticCheck" />.</param>
/// <param name="Name">The probe name reported to operators and health hosts.</param>
[DebuggerDisplay("{Name} ({ImplementationType.Name})")]
public sealed record DiagnosticCheckDescriptor(Type ImplementationType, string Name);
