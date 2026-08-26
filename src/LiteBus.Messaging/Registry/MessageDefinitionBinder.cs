using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     Reads the metadata facets declared by a message definition type.
/// </summary>
internal static class MessageDefinitionBinder
{
    /// <summary>
    ///     The open generic facet contract implemented by every message definition.
    /// </summary>
    private static readonly Type FacetContract = typeof(IMessageDefinition<,>);

    /// <summary>
    ///     Determines whether a type is a concrete message definition that can be instantiated and applied.
    /// </summary>
    /// <param name="type">The candidate type.</param>
    /// <returns><see langword="true" /> when the type declares one or more metadata facets.</returns>
    public static bool IsDefinition(Type type)
    {
        return type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
               && typeof(IMessageDefinition).IsAssignableFrom(type);
    }

    /// <summary>
    ///     Instantiates a definition type and reads every metadata facet it declares.
    /// </summary>
    /// <param name="definitionType">The concrete definition type to bind.</param>
    /// <returns>The facets declared by the definition.</returns>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when the definition has no parameterless constructor, or declares no facet.
    /// </exception>
    [RequiresUnreferencedCode("Message definition binding reads facet contracts via reflection.")]
    public static IReadOnlyList<MessageDefinitionFacet> Bind(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors
                                    | DynamicallyAccessedMemberTypes.NonPublicConstructors
                                    | DynamicallyAccessedMemberTypes.Interfaces)]
        Type definitionType)
    {
        ArgumentNullException.ThrowIfNull(definitionType);

        var facetContracts = new List<Type>();

        foreach (var contract in definitionType.GetInterfaces())
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == FacetContract)
            {
                facetContracts.Add(contract);
            }
        }

        if (facetContracts.Count == 0)
        {
            throw new LiteBusConfigurationException(
                $"The message definition '{definitionType.Name}' declares no metadata facet. Implement at least one "
                + $"'{FacetContract.Name}' facet, such as IAuditDefinition<TMessage>, or a facet of your own.");
        }

        var instance = CreateInstance(definitionType);
        var facets = new List<MessageDefinitionFacet>(facetContracts.Count);

        foreach (var contract in facetContracts)
        {
            var arguments = contract.GetGenericArguments();
            var messageType = arguments[0];
            var valueType = arguments[1];

            var valueProperty = contract.GetProperty(nameof(IMessageDefinition<object, object>.Value))
                                ?? throw new LiteBusConfigurationException(
                                    $"The facet '{contract.Name}' on '{definitionType.Name}' does not expose a readable value.");

            var value = valueProperty.GetValue(instance)
                        ?? throw new LiteBusConfigurationException(
                            $"The message definition '{definitionType.Name}' returned a null value for facet '{valueType.Name}'.");

            facets.Add(new MessageDefinitionFacet(
                messageType.NormalizeMessageRegistrationType(),
                valueType,
                value,
                definitionType));
        }

        return facets;
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
