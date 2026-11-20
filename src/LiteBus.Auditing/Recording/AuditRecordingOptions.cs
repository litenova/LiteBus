using System;
using System.Collections.Generic;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Auditing.Recording;

/// <summary>
///     Holds what <see cref="AuditingModuleBuilder" /> recorded about turning mediations into audit records: which axes
///     produce records, and the trail, writer, actor resolver and outcome mapper an application supplied.
/// </summary>
/// <remarks>
///     Registered as a singleton so the <c>litebus.audit.trail</c> diagnostic check and the composition section read
///     the same configuration the module built from.
/// </remarks>
internal sealed class AuditRecordingOptions
{
    /// <summary>
    ///     The composition checks over audit declarations, run once every module has built.
    /// </summary>
    private readonly List<Action<ICompositionModel>> _checks = [];

    /// <summary>
    ///     Gets or sets a value indicating whether commands produce audit records.
    /// </summary>
    public bool Commands { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether queries produce audit records.
    /// </summary>
    public bool Queries { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether events produce audit records.
    /// </summary>
    public bool Events { get; set; }

    /// <summary>
    ///     Gets a value indicating whether any axis produces audit records.
    /// </summary>
    public bool AnyAxis => Commands || Queries || Events;

    /// <summary>
    ///     Gets or sets the application's own trail, as an implementation type or an instance, or
    ///     <see langword="null" /> when an audit store supplies the trail.
    /// </summary>
    public object? Trail { get; set; }

    /// <summary>
    ///     Gets or sets the lifetime an application trail type is registered with.
    /// </summary>
    public InstanceLifetime TrailLifetime { get; set; } = InstanceLifetime.Scoped;

    /// <summary>
    ///     Gets or sets the record writer that replaces the built-in one, as a type or an instance, or
    ///     <see langword="null" /> for the built-in writer.
    /// </summary>
    public object? RecordWriter { get; set; }

    /// <summary>
    ///     Gets or sets the lifetime a record writer type is registered with.
    /// </summary>
    public InstanceLifetime RecordWriterLifetime { get; set; } = InstanceLifetime.Scoped;

    /// <summary>
    ///     Gets or sets the actor resolver, as a type or an instance, or <see langword="null" /> to attribute records
    ///     from the execution identity's principal.
    /// </summary>
    public object? ActorResolver { get; set; }

    /// <summary>
    ///     Gets or sets the lifetime an actor resolver type is registered with.
    /// </summary>
    public InstanceLifetime ActorResolverLifetime { get; set; } = InstanceLifetime.Scoped;

    /// <summary>
    ///     Gets or sets the outcome mapper that replaces the built-in one, or <see langword="null" /> for the default.
    /// </summary>
    public IAuditOutcomeMapper? OutcomeMapper { get; set; }

    /// <summary>
    ///     Gets the composition checks over audit declarations, in the order they were added.
    /// </summary>
    public IReadOnlyList<Action<ICompositionModel>> Checks => _checks;

    /// <summary>
    ///     Gets a description of the replacing record writer, such as <c>MyWriter (Scoped)</c>, or
    ///     <see langword="null" /> when the built-in writer builds the records.
    /// </summary>
    public string? RecordWriterDescription => Describe(RecordWriter, RecordWriterLifetime);

    /// <summary>
    ///     Gets a description of the application trail, or <see langword="null" /> when a store supplies it.
    /// </summary>
    public string? TrailDescription => Describe(Trail, TrailLifetime);

    /// <summary>
    ///     Adds a composition check over the audit declarations.
    /// </summary>
    /// <param name="check">The check, which throws to fail composition.</param>
    public void AddCheck(Action<ICompositionModel> check)
    {
        ArgumentNullException.ThrowIfNull(check);

        _checks.Add(check);
    }

    /// <summary>
    ///     Describes a configured component as its type name and lifetime.
    /// </summary>
    /// <param name="component">The implementation type or instance, or <see langword="null" />.</param>
    /// <param name="lifetime">The lifetime a type is registered with.</param>
    /// <returns>The description, or <see langword="null" /> when nothing was configured.</returns>
    private static string? Describe(object? component, InstanceLifetime lifetime)
    {
        return component switch
        {
            null => null,
            Type type => $"{type.Name} ({lifetime})",
            var instance => $"{instance.GetType().Name} (Singleton)"
        };
    }
}
