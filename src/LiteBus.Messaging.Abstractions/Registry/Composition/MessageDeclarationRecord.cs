using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     One effective declaration value and where it came from.
/// </summary>
/// <param name="DeclarationType">The metadata value type.</param>
/// <param name="Value">The effective value.</param>
/// <param name="Source">Which source supplied it.</param>
/// <param name="DeclaredFor">The message type or scope the source named.</param>
/// <param name="SourceType">The definition, catalog source, or attribute type, when one exists.</param>
/// <param name="Rationale">The exemption rationale, when the source is an exemption.</param>
/// <remarks>
///     <para>
///         Read through <see cref="ICompositionModel.DeclarationsFor" />. It carries the resolved value the pipeline
///         will see together with its provenance, so a composition check reports which source won for each value rather
///         than only what won. The exported snapshot carries the same table for CI diffs and compliance records.
///     </para>
///     <para>
///         <paramref name="DeclaredFor" /> is the type the source named, which may be a base type or a marker interface
///         covering the message rather than the message itself. <paramref name="SourceType" /> names the definition
///         class, catalog source, or attribute type when the source is one, and is <see langword="null" /> for defaults
///         and conventions. <paramref name="Rationale" /> carries the exemption reason when <paramref name="Source" /> is
///         <see cref="DeclarationSourceKind.Exemption" />, and is <see langword="null" /> otherwise.
///     </para>
/// </remarks>
public sealed record MessageDeclarationRecord(
    Type DeclarationType,
    object Value,
    DeclarationSourceKind Source,
    Type DeclaredFor,
    Type? SourceType,
    string? Rationale);
