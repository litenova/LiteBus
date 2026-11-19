using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     One resolved port implementation and whether it is the framework default or an application replacement.
/// </summary>
/// <param name="PortType">The port interface the composition resolved.</param>
/// <param name="ImplementationType">The implementation the composition resolved it to.</param>
/// <param name="IsDefault">
///     <see langword="true" /> when the implementation is the framework default; <see langword="false" /> when the
///     application replaced it.
/// </param>
/// <remarks>
///     <para>
///         Read through <see cref="ICompositionModel.Ports" />. Ports are the interfaces an application implements and
///         registers, such as the tenant metadata provider or a permission lookup. Naming each one and whether it is
///         still the default turns the most consequential implicit behavior in the composition, what the host runs when
///         the application registered nothing, into a line in the snapshot.
///     </para>
/// </remarks>
public sealed record CompositionPortEntry(
    Type PortType,
    Type ImplementationType,
    bool IsDefault);
