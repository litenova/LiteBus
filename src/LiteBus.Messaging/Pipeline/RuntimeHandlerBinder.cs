using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging;

/// <summary>
///     Closes a handler registered by its open generic definition over the runtime message, producing the handler type
///     to resolve and the contract to invoke it through.
/// </summary>
/// <remarks>
///     <para>
///         A handler for a generic message, such as <c>ListQueryHandler&lt;T&gt; : IQueryHandler&lt;ListQuery&lt;T&gt;,
///         List&lt;T&gt;&gt;</c>, is registered by its open definition, so its contract is written in the handler's own
///         type parameters. The message type in that contract is matched against the runtime message type, its base
///         types, and its interfaces, and each type parameter is bound to the type found in the same place. The handler
///         type and the contract are both closed from those bindings, so a type parameter may be nested in a type
///         argument, appear in the result type, or be declared in a different order than the message declares its own.
///     </para>
///     <para>
///         A handler whose message type matches no shape of the runtime message, or whose constraints reject the bound
///         types, does not apply to that message. The pipeline leaves it out, which is how two handlers of one generic
///         message divide its closed types between them by shape or by constraint.
///     </para>
/// </remarks>
internal static class RuntimeHandlerBinder
{
    /// <summary>
    ///     Holds the closure of each open handler over each runtime message type it was dispatched for, or
    ///     <see langword="null" /> when the handler does not apply to that message, for the lifetime of the process.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type HandlerType, Type ContractType, Type MessageType), RuntimeHandlerClosure?> Closures = new();

    /// <summary>
    ///     Determines whether a descriptor records an open generic handler that must be closed for each runtime message.
    /// </summary>
    /// <param name="descriptor">The handler descriptor.</param>
    /// <returns>
    ///     <see langword="true" /> when the handler type is a generic type definition; <see langword="false" /> for a
    ///     handler the registry already closed, or one that was never generic.
    /// </returns>
    public static bool IsOpen(IHandlerDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return descriptor.HandlerType.IsGenericTypeDefinition;
    }

    /// <summary>
    ///     Closes an open handler over the runtime message type, or reports that the handler does not apply to it.
    /// </summary>
    /// <param name="descriptor">The descriptor of a handler for which <see cref="IsOpen" /> is true.</param>
    /// <param name="runtimeMessageType">The concrete type of the message being mediated.</param>
    /// <returns>
    ///     The closed handler type and contract, or <see langword="null" /> when the handler's message type matches no
    ///     shape of the runtime message or its constraints reject the bound types.
    /// </returns>
    /// <exception cref="PipelineContractException">
    ///     A type parameter of the handler does not appear in the message type of its contract, so no message can bind
    ///     it, or the runtime message implements the handled message type more than once with different type arguments.
    /// </exception>
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    public static RuntimeHandlerClosure? TryClose(IHandlerDescriptor descriptor, Type runtimeMessageType)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(runtimeMessageType);

        var key = (descriptor.HandlerType, descriptor.ContractType, runtimeMessageType);

        if (Closures.TryGetValue(key, out var cached))
        {
            return cached;
        }

        // A configuration error throws here and is not cached, so every dispatch of the message reports it.
        var closure = Bind(descriptor.HandlerType, descriptor.ContractType, runtimeMessageType);
        Closures.TryAdd(key, closure);

        return closure;
    }

    /// <summary>
    ///     Closes an open handler over the runtime message type.
    /// </summary>
    /// <param name="descriptor">The descriptor of a handler for which <see cref="IsOpen" /> is true.</param>
    /// <param name="runtimeMessageType">The concrete type of the message being mediated.</param>
    /// <returns>The closed handler type and contract.</returns>
    /// <exception cref="PipelineContractException">
    ///     The handler cannot be closed over the runtime message, for any reason <see cref="TryClose" /> throws for or
    ///     because the handler does not apply to the message.
    /// </exception>
    /// <remarks>
    ///     The pipeline resolves only handlers that <see cref="TryClose" /> closed, so a handler that does not apply never
    ///     reaches this method during mediation.
    /// </remarks>
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    public static RuntimeHandlerClosure Close(IHandlerDescriptor descriptor, Type runtimeMessageType)
    {
        return TryClose(descriptor, runtimeMessageType)
               ?? throw new PipelineContractException(
                   $"The handler '{descriptor.HandlerType.Name}' was registered under the contract "
                   + $"'{descriptor.ContractType.Name}', which does not fit message '{runtimeMessageType.Name}'.");
    }

    /// <summary>
    ///     Matches the contract's message type against the runtime message and closes the handler from the bindings.
    /// </summary>
    /// <param name="handlerType">The open generic handler definition.</param>
    /// <param name="contractType">The contract written in the handler's type parameters.</param>
    /// <param name="runtimeMessageType">The concrete type of the message being mediated.</param>
    /// <returns>The closure, or <see langword="null" /> when the handler does not apply to the message.</returns>
    /// <exception cref="PipelineContractException">The handler cannot be bound or the binding is ambiguous.</exception>
    private static RuntimeHandlerClosure? Bind(Type handlerType, Type contractType, Type runtimeMessageType)
    {
        // Every pipeline contract takes the message type as its first type argument.
        var messagePattern = contractType.IsGenericType ? contractType.GetGenericArguments()[0] : contractType;

        ThrowIfAnyTypeParameterIsUnbindable(handlerType, contractType, messagePattern);

        RuntimeHandlerClosure? match = null;
        Type? matchedShape = null;

        foreach (var shape in CandidateShapes(messagePattern, runtimeMessageType))
        {
            var bindings = new Dictionary<Type, Type>();

            if (!TryMatch(messagePattern, shape, bindings))
            {
                continue;
            }

            var closure = TryCloseWith(handlerType, contractType, bindings);

            if (closure is null || closure == match)
            {
                continue;
            }

            if (match is not null)
            {
                throw new PipelineContractException(
                    $"The handler '{handlerType.Name}' handles '{Describe(messagePattern)}', which message "
                    + $"'{runtimeMessageType.Name}' implements as both '{Describe(matchedShape!)}' and "
                    + $"'{Describe(shape)}'. Register a handler for the message itself, or implement the generic "
                    + "message type once.");
            }

            match = closure;
            matchedShape = shape;
        }

        return match;
    }

    /// <summary>
    ///     Rejects a handler with a type parameter that the message type of its contract does not contain.
    /// </summary>
    /// <param name="handlerType">The open generic handler definition.</param>
    /// <param name="contractType">The contract written in the handler's type parameters.</param>
    /// <param name="messagePattern">The message type of the contract.</param>
    /// <exception cref="PipelineContractException">At least one type parameter can never be bound.</exception>
    private static void ThrowIfAnyTypeParameterIsUnbindable(Type handlerType, Type contractType, Type messagePattern)
    {
        var bindable = new HashSet<Type>();
        CollectGenericParameters(messagePattern, bindable);

        var unbindable = handlerType.GetGenericArguments().Where(parameter => !bindable.Contains(parameter)).ToList();

        if (unbindable.Count == 0)
        {
            return;
        }

        throw new PipelineContractException(
            $"The handler '{handlerType.Name}' declares the type parameter(s) "
            + $"'{string.Join("', '", unbindable.Select(parameter => parameter.Name))}', which the message type "
            + $"'{Describe(messagePattern)}' of its contract '{contractType.Name}' does not contain. A generic handler "
            + "is closed from the message it handles, so every type parameter must appear in that message type.");
    }

    /// <summary>
    ///     Lists the runtime message type, its base types, and its interfaces that share the pattern's definition.
    /// </summary>
    /// <param name="messagePattern">The message type of the contract.</param>
    /// <param name="runtimeMessageType">The concrete type of the message being mediated.</param>
    /// <returns>The candidate shapes, the message type and its base types first.</returns>
    private static IEnumerable<Type> CandidateShapes(Type messagePattern, Type runtimeMessageType)
    {
        if (messagePattern.IsGenericParameter)
        {
            yield return runtimeMessageType;
            yield break;
        }

        var definition = messagePattern.IsGenericType ? messagePattern.GetGenericTypeDefinition() : messagePattern;

        for (var current = runtimeMessageType; current is not null; current = current.BaseType)
        {
            if (HasDefinition(current, definition))
            {
                yield return current;
            }
        }

        foreach (var implemented in runtimeMessageType.GetInterfaces())
        {
            if (HasDefinition(implemented, definition))
            {
                yield return implemented;
            }
        }
    }

    /// <summary>
    ///     Determines whether a type is, or is constructed from, a definition.
    /// </summary>
    /// <param name="type">The candidate type.</param>
    /// <param name="definition">The generic definition, or a non-generic type.</param>
    /// <returns><see langword="true" /> when the type has the definition.</returns>
    private static bool HasDefinition(Type type, Type definition)
    {
        return type == definition || (type.IsGenericType && type.GetGenericTypeDefinition() == definition);
    }

    /// <summary>
    ///     Matches a type written in generic parameters against a closed type, binding each parameter it contains.
    /// </summary>
    /// <param name="pattern">The type that may contain generic parameters.</param>
    /// <param name="closed">The closed type to match.</param>
    /// <param name="bindings">The bindings found so far, extended in place.</param>
    /// <returns>
    ///     <see langword="true" /> when the closed type has the pattern's structure and every parameter binds to one
    ///     type; <see langword="false" /> otherwise.
    /// </returns>
    private static bool TryMatch(Type pattern, Type closed, Dictionary<Type, Type> bindings)
    {
        if (pattern.IsGenericParameter)
        {
            if (bindings.TryGetValue(pattern, out var bound))
            {
                return bound == closed;
            }

            bindings.Add(pattern, closed);
            return true;
        }

        if (!pattern.ContainsGenericParameters)
        {
            return pattern == closed;
        }

        if (pattern.IsArray)
        {
            return closed.IsArray
                   && pattern.IsSZArray == closed.IsSZArray
                   && pattern.GetArrayRank() == closed.GetArrayRank()
                   && TryMatch(pattern.GetElementType()!, closed.GetElementType()!, bindings);
        }

        if (!pattern.IsGenericType || !closed.IsGenericType ||
            pattern.GetGenericTypeDefinition() != closed.GetGenericTypeDefinition())
        {
            return false;
        }

        var patternArguments = pattern.GetGenericArguments();
        var closedArguments = closed.GetGenericArguments();

        for (var index = 0; index < patternArguments.Length; index++)
        {
            if (!TryMatch(patternArguments[index], closedArguments[index], bindings))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Closes the handler type and contract from a complete set of bindings.
    /// </summary>
    /// <param name="handlerType">The open generic handler definition.</param>
    /// <param name="contractType">The contract written in the handler's type parameters.</param>
    /// <param name="bindings">A binding for every type parameter of the handler.</param>
    /// <returns>The closure, or <see langword="null" /> when the handler's constraints reject the bound types.</returns>
    private static RuntimeHandlerClosure? TryCloseWith(Type handlerType, Type contractType, Dictionary<Type, Type> bindings)
    {
        try
        {
            // The CLR evaluates the handler's constraints against the bound types, including constraints such as
            // IComparable<T> that cannot be checked before T is known.
            return new RuntimeHandlerClosure(Substitute(handlerType, bindings), Substitute(contractType, bindings));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Replaces each generic parameter in a type with its binding.
    /// </summary>
    /// <param name="type">The type that may contain generic parameters.</param>
    /// <param name="bindings">A binding for every generic parameter the type contains.</param>
    /// <returns>The closed type.</returns>
    /// <exception cref="ArgumentException">A bound type violates a constraint of the generic definition it closes.</exception>
    private static Type Substitute(Type type, Dictionary<Type, Type> bindings)
    {
        if (type.IsGenericParameter)
        {
            return bindings[type];
        }

        if (!type.ContainsGenericParameters)
        {
            return type;
        }

        if (type.IsArray)
        {
            var element = Substitute(type.GetElementType()!, bindings);
            return type.IsSZArray ? element.MakeArrayType() : element.MakeArrayType(type.GetArrayRank());
        }

        var arguments = Array.ConvertAll(type.GetGenericArguments(), argument => Substitute(argument, bindings));
        return type.GetGenericTypeDefinition().MakeGenericType(arguments);
    }

    /// <summary>
    ///     Adds every generic parameter a type contains to a set.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <param name="parameters">The set the parameters are added to.</param>
    private static void CollectGenericParameters(Type type, HashSet<Type> parameters)
    {
        if (type.IsGenericParameter)
        {
            parameters.Add(type);
            return;
        }

        if (type.HasElementType)
        {
            CollectGenericParameters(type.GetElementType()!, parameters);
            return;
        }

        if (!type.IsGenericType)
        {
            return;
        }

        foreach (var argument in type.GetGenericArguments())
        {
            CollectGenericParameters(argument, parameters);
        }
    }

    /// <summary>
    ///     Formats a type with its type arguments for an error message.
    /// </summary>
    /// <param name="type">The type to format.</param>
    /// <returns>A readable name such as <c>IEntityChanged&lt;Customer&gt;</c>.</returns>
    private static string Describe(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);

        if (tick >= 0)
        {
            name = name[..tick];
        }

        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>";
    }
}
