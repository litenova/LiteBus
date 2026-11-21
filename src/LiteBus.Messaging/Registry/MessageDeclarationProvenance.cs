using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     One effective metadata value and the source that supplied it, as the message metadata resolved it.
/// </summary>
/// <param name="KeyType">The metadata value type the value is keyed by.</param>
/// <param name="Value">The effective value the pipeline reads.</param>
/// <param name="Source">Which source supplied the value.</param>
/// <param name="DeclaredFor">The message type or scope the source named.</param>
/// <param name="SourceType">The definition or attribute type, when the source is one.</param>
/// <remarks>
///     <para>
///         Read through the message metadata by the composition model, which turns each entry into a
///         <see cref="MessageDeclarationRecord" />. The model expands the <see cref="DeclarationExemptions" /> value
///         into one exemption record per exemption it holds rather than reporting the set itself, because the set is
///         the envelope and the exemptions are what a composition check and the exported snapshot assert over.
///     </para>
///     <para>
///         <paramref name="DeclaredFor" /> is the type the source named, which may be a base type or a marker
///         interface covering the message rather than the message itself. <paramref name="SourceType" /> names the
///         definition class or attribute type when <paramref name="Source" /> is
///         <see cref="DeclarationSourceKind.Definition" /> or <see cref="DeclarationSourceKind.Attribute" />, and is
///         <see langword="null" /> for defaults and conventions, which are composition data and functions rather
///         than types.
///     </para>
/// </remarks>
internal sealed record MessageDeclarationProvenance(
    Type KeyType,
    object Value,
    DeclarationSourceKind Source,
    Type DeclaredFor,
    Type? SourceType);
