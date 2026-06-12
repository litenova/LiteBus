using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Captures contract registrations as data for deferred application to a live
///     <see cref="IMessageContractRegistry" />. Used by module builders that execute
///     during composite module child declaration before the DI context is available.
/// </summary>
public sealed class MessageContractBuilder : IContractWriter
{
    /// <summary>
    ///     Deferred registrations collected before <see cref="ApplyTo" /> runs.
    /// </summary>
    private readonly List<PendingRegistration> _pending = [];

    /// <summary>
    ///     Gets a value indicating whether any registrations have been captured.
    /// </summary>
    /// <value><see langword="true" /> when at least one registration was recorded; otherwise <see langword="false" />.</value>
    public bool HasRegistrations => _pending.Count > 0;

    /// <inheritdoc />
    public IContractWriter Register<TMessage>(string name, int version = 1)
        where TMessage : notnull
    {
        return Register(typeof(TMessage), name, version);
    }

    /// <inheritdoc />
    public IContractWriter Register(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type messageType,
        string name,
        int version = 1)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (messageType.ContainsGenericParameters)
        {
            throw new ArgumentException(
                "message contracts must use a closed message type. Register each closed generic message shape with its own stable contract name and version.",
                nameof(messageType));
        }

        if (version <= 0)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(version, 0, nameof(version));
        }

        _pending.Add(new PendingRegistration(messageType, name, version));
        return this;
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Scans assemblies for MessageContractAttribute-decorated message types.")]
    public IContractWriter AddFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var type in assembly.GetTypes())
        {
            if (type is not { IsAbstract: false } || type.ContainsGenericParameters)
            {
                continue;
            }

            var attribute = type.GetCustomAttribute<MessageContractAttribute>(false);

            if (attribute is not null)
            {
                Register(type, attribute.Name, attribute.Version);
            }
        }

        return this;
    }

    /// <summary>
    ///     Replays all captured registrations against <paramref name="registry" />.
    /// </summary>
    /// <param name="registry">The live contract registry created during module build.</param>
    public void ApplyTo(IMessageContractRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        foreach (var pending in _pending)
        {
            registry.Register(pending.MessageType, pending.Name, pending.Version);
        }
    }

    /// <summary>
    ///     One deferred contract registration captured during builder configuration.
    /// </summary>
    /// <param name="MessageType">The CLR message type to register.</param>
    /// <param name="Name">The stable contract name.</param>
    /// <param name="Version">The positive contract version.</param>
    private sealed record PendingRegistration(Type MessageType, string Name, int Version);
}