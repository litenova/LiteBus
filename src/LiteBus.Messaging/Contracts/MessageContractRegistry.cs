using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Default in-memory registry for message contracts.
/// </summary>
public sealed class MessageContractRegistry : IMessageContractRegistry, IMessageContractRegistrar
{
    private readonly Dictionary<Type, MessageContract> _contractsByType = [];
    private readonly Dictionary<(string Name, int Version), Type> _typesByContract = [];
    private readonly object _syncRoot = new();

    /// <inheritdoc />
    public IMessageContractRegistrar Register<TMessage>(string name, int version = 1)
        where TMessage : notnull
    {
        return Register(typeof(TMessage), name, version);
    }

    /// <inheritdoc />
    public IMessageContractRegistrar Register(Type messageType, string name, int version = 1)
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
            throw new ArgumentOutOfRangeException(nameof(version), version, "Contract version must be greater than zero.");
        }

        var contract = new MessageContract
        {
            Name = name,
            Version = version,
            MessageType = messageType
        };

        lock (_syncRoot)
        {
            if (_contractsByType.TryGetValue(messageType, out var existingContract) && existingContract != contract)
            {
                throw new MessageContractAlreadyRegisteredException(
                    $"Message type '{messageType.FullName ?? messageType.Name}' is already registered as '{existingContract.Name}' version {existingContract.Version}.");
            }

            var contractKey = (name, version);

            if (_typesByContract.TryGetValue(contractKey, out var existingType) && existingType != messageType)
            {
                throw new MessageContractAlreadyRegisteredException(
                    $"Message contract '{name}' version {version} is already registered for '{existingType.FullName ?? existingType.Name}'.");
            }

            _contractsByType[messageType] = contract;
            _typesByContract[contractKey] = messageType;
        }

        return this;
    }

    /// <inheritdoc />
    public MessageContract GetContract(Type messageType)
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
}