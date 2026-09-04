using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The set of metadata types one message deliberately declares nothing for.
/// </summary>
/// <remarks>
///     <para>
///         This is one metadata value holding many exemptions, rather than one value per exemption, because metadata is
///         keyed by value type and a message may be exempt from several requirements at once.
///     </para>
///     <para>
///         Declare exemptions with <see cref="DeclarationExemptAttribute" />, which may be applied more than once and is
///         aggregated into a single value, or contribute this type from a definition class the way any other declaration
///         is contributed. A definition wins over the attributes, replacing the whole set rather than adding to it.
///     </para>
/// </remarks>
public sealed class DeclarationExemptions
{
    /// <summary>
    ///     The exemptions keyed by the metadata type each one covers.
    /// </summary>
    private readonly Dictionary<Type, DeclarationExemption> _byType;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DeclarationExemptions" /> class.
    /// </summary>
    /// <param name="exemptions">The exemptions the message declares.</param>
    /// <exception cref="ArgumentNullException"><paramref name="exemptions" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     Two exemptions for the same metadata type keep the first, because the second says nothing new: the message is
    ///     exempt either way, and failing composition over a duplicated rationale would be pedantry rather than a
    ///     safeguard.
    /// </remarks>
    public DeclarationExemptions(IEnumerable<DeclarationExemption> exemptions)
    {
        ArgumentNullException.ThrowIfNull(exemptions);

        _byType = [];

        foreach (var exemption in exemptions)
        {
            ArgumentNullException.ThrowIfNull(exemption);
            _byType.TryAdd(exemption.DeclarationType, exemption);
        }
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="DeclarationExemptions" /> class from a single exemption.
    /// </summary>
    /// <param name="declarationType">The metadata value type the message is exempt from declaring.</param>
    /// <param name="rationale">The recorded reason the message is exempt.</param>
    public DeclarationExemptions(Type declarationType, string rationale)
        : this([new DeclarationExemption(declarationType, rationale)])
    {
    }

    /// <summary>
    ///     Gets every exemption the message declares.
    /// </summary>
    public IReadOnlyCollection<DeclarationExemption> Values => _byType.Values;

    /// <summary>
    ///     Determines whether the message is exempt from declaring a value of the given type.
    /// </summary>
    /// <param name="declarationType">The metadata value type to check.</param>
    /// <returns><see langword="true" /> when an exemption covers that type.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="declarationType" /> is <see langword="null" />.</exception>
    public bool Covers(Type declarationType)
    {
        ArgumentNullException.ThrowIfNull(declarationType);
        return _byType.ContainsKey(declarationType);
    }

    /// <summary>
    ///     Determines whether the message is exempt from declaring a value of type <typeparamref name="TValue" />.
    /// </summary>
    /// <typeparam name="TValue">The metadata value type to check.</typeparam>
    /// <returns><see langword="true" /> when an exemption covers that type.</returns>
    public bool Covers<TValue>()
        where TValue : notnull
    {
        return _byType.ContainsKey(typeof(TValue));
    }

    /// <summary>
    ///     Attempts to read the exemption covering a metadata value type.
    /// </summary>
    /// <param name="declarationType">The metadata value type to look up.</param>
    /// <param name="exemption">When this method returns <see langword="true" />, the exemption and its rationale.</param>
    /// <returns><see langword="true" /> when an exemption covers that type.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="declarationType" /> is <see langword="null" />.</exception>
    public bool TryGet(Type declarationType, [MaybeNullWhen(false)] out DeclarationExemption exemption)
    {
        ArgumentNullException.ThrowIfNull(declarationType);
        return _byType.TryGetValue(declarationType, out exemption);
    }
}
