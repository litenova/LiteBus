using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     Reads the metadata declared by a message definition type.
/// </summary>
internal static class MessageDefinitionBinder
{
    /// <summary>
    ///     The open generic contract implemented once per value type a definition declares.
    /// </summary>
    private static readonly Type DefinitionContract = typeof(IMessageDefinition<,>);

    /// <summary>
    ///     Determines whether a type is a concrete message definition that can be instantiated and applied.
    /// </summary>
    /// <param name="type">The candidate type.</param>
    /// <returns><see langword="true" /> when the type declares metadata for one or more messages.</returns>
    public static bool IsDefinition(Type type)
    {
        return type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
               && typeof(IMessageDefinition).IsAssignableFrom(type);
    }

    /// <summary>
    ///     Instantiates a definition type and reads every value it declares.
    /// </summary>
    /// <param name="definitionType">The concrete definition type to bind.</param>
    /// <returns>The declarations the definition contributes.</returns>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when the definition has no parameterless constructor, declares nothing, or declares a null value.
    /// </exception>
    [RequiresUnreferencedCode("Message definition binding reads declaration contracts via reflection.")]
    public static IReadOnlyList<MessageDeclaration> Bind(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors
                                    | DynamicallyAccessedMemberTypes.NonPublicConstructors
                                    | DynamicallyAccessedMemberTypes.Interfaces)]
        Type definitionType)
    {
        ArgumentNullException.ThrowIfNull(definitionType);

        var contracts = new List<Type>();

        foreach (var contract in definitionType.GetInterfaces())
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == DefinitionContract)
            {
                contracts.Add(contract);
            }
        }

        if (contracts.Count == 0)
        {
            throw new LiteBusConfigurationException(
                $"The message definition '{definitionType.Name}' declares no metadata. Implement at least one "
                + $"'{DefinitionContract.Name}', such as IAuditDefinition<TMessage>, or one of your own.");
        }

        var instance = CreateInstance(definitionType);
        var declarations = new List<MessageDeclaration>(contracts.Count);

        foreach (var contract in contracts)
        {
            var arguments = contract.GetGenericArguments();
            var messageType = arguments[0];
            var valueType = arguments[1];

            var valueProperty = contract.GetProperty(nameof(IMessageDefinition<object, object>.Value))
                                ?? throw new LiteBusConfigurationException(
                                    $"The declaration '{contract.Name}' on '{definitionType.Name}' does not expose a readable value.");

            var value = valueProperty.GetValue(instance)
                        ?? throw new LiteBusConfigurationException(
                            $"The message definition '{definitionType.Name}' returned a null value for '{valueType.Name}'.");

            declarations.Add(new MessageDeclaration(
                messageType.NormalizeMessageRegistrationType(),
                valueType,
                value,
                definitionType));
        }

        return declarations;
    }

    /// <summary>
    ///     Creates a definition instance using its parameterless constructor, public or not.
    /// </summary>
    /// <param name="definitionType">The definition type to instantiate.</param>
    /// <returns>The definition instance.</returns>
    private static object CreateInstance(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors
                                    | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
        Type definitionType)
    {
        var constructor = definitionType.GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);

        if (constructor is null)
        {
            throw new LiteBusConfigurationException(
                $"The message definition '{definitionType.Name}' must expose a parameterless constructor. "
                + "Definitions are declarative and are instantiated once during registration, so they cannot take dependencies.");
        }

        return constructor.Invoke(parameters: null);
    }
}
