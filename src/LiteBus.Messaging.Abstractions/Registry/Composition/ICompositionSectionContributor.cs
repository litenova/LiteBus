namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Contributes one named section to the composition model and its exported snapshot.
/// </summary>
/// <remarks>
///     <para>
///         This is the interface a feature bridge implements so the core reads its registrations without referencing the
///         feature package. Refusal mappings, recurring registrations and other feature-owned composition each arrive as
///         one section. The core collects the contributors, exposes them on
///         <see cref="ICompositionModel.Sections" /> in ordinal <see cref="SectionName" /> order, and serializes each
///         section under its name in <see cref="ICompositionModel.ToJson" />.
///     </para>
///     <para>
///         The value <see cref="GetData" /> returns must be serializable by <c>System.Text.Json</c>: records,
///         dictionaries with ordinal string keys, lists, and primitives. Dictionaries with non-string keys and values
///         that serialize by reference break the deterministic export.
///     </para>
/// </remarks>
public interface ICompositionSectionContributor
{
    /// <summary>
    ///     Gets the section name the contribution is exposed and serialized under.
    /// </summary>
    /// <value>The snapshot section key, such as <c>recurring</c>.</value>
    string SectionName { get; }

    /// <summary>
    ///     Gets the snapshot data for this section.
    /// </summary>
    /// <returns>The section data, or <see langword="null" /> when the feature registered nothing.</returns>
    object? GetData();
}
