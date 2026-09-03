using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
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
    ///     The open generic contract implemented once per message a definition describes in full.
    /// </summary>
    private static readonly Type DescribeContract = typeof(IMessageDefinition<>);

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
    /// <exception cref="MessageDeclarationException">
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
        var describeContracts = new List<Type>();

        foreach (var contract in definitionType.GetInterfaces())
        {
            if (!contract.IsGenericType)
            {
                continue;
            }

            var definition = contract.GetGenericTypeDefinition();

            if (definition == DefinitionContract)
            {
                contracts.Add(contract);
            }
            else if (definition == DescribeContract)
            {
                describeContracts.Add(contract);
            }
        }

        if (contracts.Count == 0 && describeContracts.Count == 0)
        {
            throw new MessageDeclarationException(
                $"The message definition '{definitionType.Name}' declares no metadata. Implement "
                + "IMessageDefinition<TMessage> and declare in Describe, or implement at least one "
                + $"'{DefinitionContract.Name}', such as IAuditDefinition<TMessage>, or one of your own.");
        }

        var instance = CreateInstance(definitionType);
        var declarations = new List<MessageDeclaration>(contracts.Count + describeContracts.Count);

        foreach (var contract in describeContracts)
        {
            declarations.AddRange(Describe(instance, definitionType, contract));
        }

        foreach (var contract in contracts)
        {
            var arguments = contract.GetGenericArguments();
            var messageType = arguments[0];
            var valueType = arguments[1];

            var valueProperty = contract.GetProperty(nameof(IMessageDefinition<object, object>.Value))
                                ?? throw new MessageDeclarationException(
                                    $"The declaration '{contract.Name}' on '{definitionType.Name}' does not expose a readable value.");

            var value = valueProperty.GetValue(instance)
                        ?? throw new MessageDeclarationException(
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
    ///     Runs one <see cref="IMessageDefinition{TMessage}.Describe" /> and collects what it declared.
    /// </summary>
    /// <param name="instance">The definition instance.</param>
    /// <param name="definitionType">The definition type, named in diagnostics.</param>
    /// <param name="contract">The closed <c>IMessageDefinition&lt;TMessage&gt;</c> being read.</param>
    /// <returns>The declarations the definition contributed for that message type.</returns>
    /// <exception cref="MessageDeclarationException">
    ///     The contract exposes no <c>Describe</c> method, or the definition declared nothing.
    /// </exception>
    /// <remarks>
    ///     One class may describe several message types, so this runs once per closed contract and gives each its own
    ///     collector. Sharing one would let a declaration written for one message leak into another.
    /// </remarks>
    [RequiresUnreferencedCode("Message definition binding reads declaration contracts via reflection.")]
    private static IReadOnlyList<MessageDeclaration> Describe(object instance, Type definitionType, Type contract)
    {
        var messageType = contract.GetGenericArguments()[0].NormalizeMessageRegistrationType();

        var describe = contract.GetMethod(nameof(IMessageDefinition<object>.Describe))
                       ?? throw new MessageDeclarationException(
                           $"The declaration '{contract.Name}' on '{definitionType.Name}' does not expose a Describe method.");

        var collector = new MessageDeclarationCollector(messageType, definitionType);

        try
        {
            describe.Invoke(instance, [collector]);
        }
        catch (TargetInvocationException invocation) when (invocation.InnerException is not null)
        {
            // Describe is invoked reflectively, so anything it throws arrives wrapped. Rethrowing the inner exception
            // with its stack intact is what makes a duplicate declaration read as the configuration error it is
            // rather than as a reflection failure the author has to unwrap.
            ExceptionDispatchInfo.Capture(invocation.InnerException).Throw();
        }

        return collector.Collect();
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
            throw new MessageDeclarationException(
                $"The message definition '{definitionType.Name}' must expose a parameterless constructor. "
                + "Definitions are declarative and are instantiated once during registration, so they cannot take dependencies.");
        }

        return constructor.Invoke(parameters: null);
    }
}
