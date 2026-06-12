using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Default in-memory registry for message contracts.
/// </summary>
internal sealed class MessageContractRegistry : IMessageContractRegistry
{
    /// <summary>
    ///     Maps registered CLR message types to their stable contract metadata.
    /// </summary>
    private readonly Dictionary<Type, MessageContract> _contractsByType = [];

    /// <summary>
    ///     Serializes concurrent reads and writes to both contract lookup tables.
    /// </summary>
    private readonly object _syncRoot = new();

    /// <summary>
    ///     Maps contract name and version pairs back to registered CLR message types.
    /// </summary>
    private readonly Dictionary<(string Name, int Version), Type> _typesByContract = [];

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

        ValidateAgainstAttribute(messageType, name, version);

        var contract = new MessageContract
        {
            Name = name,
            Version = version,
            MessageType = messageType
        };

        lock (_syncRoot)
        {
            RegisterLocked(messageType, contract);
        }

        return this;
    }

    /// <inheritdoc />
    public MessageContract GetContract(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        lock (_syncRoot)
        {
            if (_contractsByType.TryGetValue(messageType, out var contract))
            {
                return contract;
            }
        }

        throw new MessageContractNotRegisteredException(messageType);
    }

    /// <inheritdoc />
    public MessageContract? TryGetContract(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        var attribute = messageType.GetCustomAttribute<MessageContractAttribute>(false);

        lock (_syncRoot)
        {
            if (_contractsByType.TryGetValue(messageType, out var contract))
            {
                return contract;
            }

            if (attribute is not null)
            {
                RegisterLocked(messageType, attribute.Name, attribute.Version);
                return _contractsByType[messageType];
            }
        }

        return null;
    }

    /// <inheritdoc />
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public Type GetMessageType(string contractName, int contractVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);

        lock (_syncRoot)
        {
            if (_typesByContract.TryGetValue((contractName, contractVersion), out var messageType))
            {
                return messageType;
            }
        }

        throw new MessageContractNotRegisteredException(contractName, contractVersion);
    }

    /// <inheritdoc />
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public Type? TryGetMessageType(string contractName, int contractVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);

        lock (_syncRoot)
        {
            if (_typesByContract.TryGetValue((contractName, contractVersion), out var messageType))
            {
                return messageType;
            }
        }

        return null;
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
    ///     Registers a contract while <see cref="_syncRoot" /> is held.
    /// </summary>
    /// <param name="messageType">The CLR message type being registered.</param>
    /// <param name="name">The contract name.</param>
    /// <param name="version">The contract version.</param>
    private void RegisterLocked(Type messageType, string name, int version)
    {
        ValidateAgainstAttribute(messageType, name, version);

        var contract = new MessageContract
        {
            Name = name,
            Version = version,
            MessageType = messageType
        };

        RegisterLocked(messageType, contract);
    }

    /// <summary>
    ///     Registers a contract while <see cref="_syncRoot" /> is held.
    /// </summary>
    /// <param name="messageType">The CLR message type being registered.</param>
    /// <param name="contract">The contract metadata to register.</param>
    private void RegisterLocked(Type messageType, MessageContract contract)
    {
        if (_contractsByType.TryGetValue(messageType, out var existingContract))
        {
            if (existingContract == contract)
            {
                return;
            }

            throw new MessageContractAlreadyRegisteredException(
                $"Message type '{messageType.FullName ?? messageType.Name}' is already registered as '{existingContract.Name}' version {existingContract.Version}.");
        }

        var contractKey = (contract.Name, contract.Version);

        if (_typesByContract.TryGetValue(contractKey, out var existingType))
        {
            if (existingType == messageType)
            {
                _contractsByType[messageType] = contract;
                return;
            }

            throw new MessageContractAlreadyRegisteredException(
                $"Message contract '{contract.Name}' version {contract.Version} is already registered for '{existingType.FullName ?? existingType.Name}'.");
        }

        _contractsByType[messageType] = contract;
        _typesByContract[contractKey] = messageType;
    }

    /// <summary>
    ///     Ensures explicit registration matches <see cref="MessageContractAttribute" /> when both are present.
    /// </summary>
    /// <param name="messageType">The CLR message type being registered.</param>
    /// <param name="name">The contract name supplied to <see cref="Register" />.</param>
    /// <param name="version">The contract version supplied to <see cref="Register" />.</param>
    private static void ValidateAgainstAttribute(Type messageType, string name, int version)
    {
        var attribute = messageType.GetCustomAttribute<MessageContractAttribute>(false);

        if (attribute is null)
        {
            return;
        }

        if (!string.Equals(attribute.Name, name, StringComparison.Ordinal) || attribute.Version != version)
        {
            throw new MessageContractMismatchException(
                messageType,
                attribute.Name,
                attribute.Version,
                name,
                version);
        }
    }
}