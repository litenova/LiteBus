using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Holds the per-type samples registered through the message module builder.
/// </summary>
/// <remarks>
///     <para>
///         A sample covers one exact type: the value object whose guards no general rule satisfies, such as an
///         address validated against a regular expression. The default sample factory consults the snapshot at
///         every level of sampling, so a sample registered for a nested member type fixes every message that
///         holds it.
///     </para>
///     <para>
///         Repeats follow the registration conflict rule. An identical repeat is a no-op, a conflicting repeat
///         fails composition naming both call sites, and a deliberate override calls
///         <see cref="MessageModuleBuilder.ReplaceSample{TMessage}" />.
///     </para>
/// </remarks>
internal sealed class MessageSampleRegistry : ICompositionSectionContributor
{
    /// <summary>
    ///     The registered samples by sampled type.
    /// </summary>
    private readonly Dictionary<Type, SampleEntry> _samples = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageSampleRegistry" /> class.
    /// </summary>
    public MessageSampleRegistry()
    {
    }

    /// <inheritdoc />
    public string SectionName => "message-samples";

    /// <summary>
    ///     Gets the number of registered samples.
    /// </summary>
    public int Count => _samples.Count;

    /// <summary>
    ///     Registers the sample built for one type.
    /// </summary>
    /// <typeparam name="TMessage">The sampled type.</typeparam>
    /// <param name="sample">The factory answering the sample.</param>
    /// <param name="source">The call site that registered the sample, as file and line.</param>
    /// <exception cref="LiteBus.Runtime.Abstractions.Exceptions.LiteBusConfigurationException">
    ///     Another call site already registered a different sample for <typeparamref name="TMessage" />.
    /// </exception>
    public void Add<TMessage>(Func<TMessage> sample, string source)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        Add(typeof(TMessage), sample, () => sample(), source);
    }

    /// <summary>
    ///     Registers the sample built for one type.
    /// </summary>
    /// <param name="messageType">The sampled type.</param>
    /// <param name="sample">The factory answering the sample.</param>
    /// <param name="source">The call site that registered the sample, as file and line.</param>
    /// <exception cref="LiteBus.Runtime.Abstractions.Exceptions.LiteBusConfigurationException">
    ///     Another call site already registered a different sample for <paramref name="messageType" />.
    /// </exception>
    public void Add(Type messageType, Func<object?> sample, string source)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        Add(messageType, sample, sample, source);
    }

    /// <summary>
    ///     Registers the sample built for one type, overwriting any sample already registered for it.
    /// </summary>
    /// <typeparam name="TMessage">The sampled type.</typeparam>
    /// <param name="sample">The factory answering the sample.</param>
    /// <param name="source">The call site that registered the sample, as file and line.</param>
    public void Replace<TMessage>(Func<TMessage> sample, string source)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        _samples[typeof(TMessage)] = new SampleEntry(sample, () => sample(), source);
    }

    /// <summary>
    ///     Registers the sample built for one type, overwriting any sample already registered for it.
    /// </summary>
    /// <param name="messageType">The sampled type.</param>
    /// <param name="sample">The factory answering the sample.</param>
    /// <param name="source">The call site that registered the sample, as file and line.</param>
    public void Replace(Type messageType, Func<object?> sample, string source)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        _samples[messageType] = new SampleEntry(sample, sample, source);
    }

    /// <summary>
    ///     Registers the sample built for one type, comparing the repeat against the original delegate.
    /// </summary>
    /// <param name="messageType">The sampled type.</param>
    /// <param name="original">The delegate the call site registered, compared by the conflict rule.</param>
    /// <param name="factory">The factory answering the sample.</param>
    /// <param name="source">The call site that registered the sample, as file and line.</param>
    /// <remarks>
    ///     The generic registration wraps the delegate to erase the type parameter, and every wrapper is a
    ///     new delegate instance. Comparing wrappers would report an identical repeat as a conflict, so the
    ///     entry keeps the original for comparison and the wrapper for invocation.
    /// </remarks>
    private void Add(Type messageType, Delegate original, Func<object?> factory, string source)
    {
        if (_samples.TryGetValue(messageType, out var existing))
        {
            RegistrationConflict.ThrowIfConflicting(
                existing.Original,
                original,
                $"A sample for '{TypeName(messageType)}'",
                existing.Source,
                source);

            return;
        }

        _samples[messageType] = new SampleEntry(original, factory, source);
    }

    /// <summary>
    ///     Reads the sample factory registered for one type.
    /// </summary>
    /// <param name="messageType">The sampled type.</param>
    /// <param name="sample">When this method returns <see langword="true" />, the factory answering the sample.</param>
    /// <returns><see langword="true" /> when a sample is registered for the type.</returns>
    public bool TryGet(Type messageType, out Func<object?>? sample)
    {
        if (_samples.TryGetValue(messageType, out var entry))
        {
            sample = entry.Factory;

            return true;
        }

        sample = null;

        return false;
    }

    /// <summary>
    ///     Copies the registered factories for the default sample factory.
    /// </summary>
    /// <returns>The sample factories by sampled type.</returns>
    /// <remarks>
    ///     The factory keeps the snapshot rather than this registry, so registrations made after the module
    ///     builds cannot change what validation samples.
    /// </remarks>
    public IReadOnlyDictionary<Type, Func<object?>> Snapshot()
    {
        Dictionary<Type, Func<object?>> snapshot = new(_samples.Count);

        foreach (var (type, entry) in _samples)
        {
            snapshot[type] = entry.Factory;
        }

        return snapshot;
    }

    /// <inheritdoc />
    public object? GetData()
    {
        if (_samples.Count == 0)
        {
            return null;
        }

        List<SortedDictionary<string, object?>> samples = new(_samples.Count);

        foreach (var type in _samples.Keys.OrderBy(static type => type.FullName ?? type.Name, StringComparer.Ordinal))
        {
            samples.Add(new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["messageType"] = type.FullName ?? type.Name,
                ["source"] = _samples[type].Source,
            });
        }

        return new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["samples"] = samples,
        };
    }

    /// <summary>
    ///     Names a type the way registrations and the snapshot identify it.
    /// </summary>
    /// <param name="type">The type to name.</param>
    /// <returns>The full type name, or the type name when no full name exists.</returns>
    private static string TypeName(Type type)
    {
        return type.FullName ?? type.Name;
    }

    /// <summary>
    ///     One registered sample and the call site that registered it.
    /// </summary>
    /// <param name="Original">The delegate the call site registered, compared by the conflict rule.</param>
    /// <param name="Factory">The factory answering the sample.</param>
    /// <param name="Source">The call site that registered the sample, as file and line.</param>
    private sealed record SampleEntry(Delegate Original, Func<object?> Factory, string Source);
}
