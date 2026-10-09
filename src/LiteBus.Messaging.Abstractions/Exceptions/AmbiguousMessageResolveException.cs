using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Thrown when a message resolve strategy finds more than one equally specific assignable descriptor.
/// </summary>
public sealed class AmbiguousMessageResolveException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AmbiguousMessageResolveException" /> class.
    /// </summary>
    /// <param name="messageType">The runtime message type that could not be resolved unambiguously.</param>
    /// <param name="resolveStrategyType">The resolve strategy that detected the ambiguity.</param>
    public AmbiguousMessageResolveException(Type messageType, Type resolveStrategyType)
        : base(BuildMessage(messageType, resolveStrategyType))
    {
        MessageType = messageType;
        ResolveStrategyType = resolveStrategyType;
        Candidates = [];
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmbiguousMessageResolveException" /> class
    ///     with the ambiguous candidate message types.
    /// </summary>
    /// <param name="messageType">The runtime message type that could not be resolved unambiguously.</param>
    /// <param name="resolveStrategyType">The resolve strategy that detected the ambiguity.</param>
    /// <param name="candidates">The equally specific registered message types that collided.</param>
    /// <remarks>
    ///     The exception message includes the candidate full names to help diagnose the issue.
    /// </remarks>
    public AmbiguousMessageResolveException(
        Type messageType,
        Type resolveStrategyType,
        IReadOnlyList<Type> candidates)
        : base(BuildMessage(messageType, resolveStrategyType, candidates))
    {
        MessageType = messageType;
        ResolveStrategyType = resolveStrategyType;
        Candidates = candidates;
    }

    /// <summary>
    ///     Gets the runtime message type that could not be resolved unambiguously.
    /// </summary>
    public Type MessageType { get; }

    /// <summary>
    ///     Gets the resolve strategy that detected the ambiguity.
    /// </summary>
    public Type ResolveStrategyType { get; }

    /// <summary>
    ///     Gets the equally specific registered message types that collided.
    /// </summary>
    public IReadOnlyList<Type> Candidates { get; }

    /// <summary>
    ///     Builds the exception message from the message type and resolve strategy type.
    /// </summary>
    /// <param name="messageType">The runtime message type.</param>
    /// <param name="resolveStrategyType">The resolve strategy type.</param>
    /// <returns>The formatted exception message.</returns>
    private static string BuildMessage(Type messageType, Type resolveStrategyType)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(resolveStrategyType);

        return
            $"More than one message descriptor is assignable from '{messageType.FullName ?? messageType.Name}' " +
            $"using resolve strategy '{resolveStrategyType.FullName ?? resolveStrategyType.Name}'. " +
            "Register handlers for a single most-derived message type or use a more specific resolve strategy.";
    }

    /// <summary>
    ///     Builds the exception message from the message type, resolve strategy type and candidates.
    /// </summary>
    /// <param name="messageType">The runtime message type.</param>
    /// <param name="resolveStrategyType">The resolve strategy type.</param>
    /// <param name="candidates">The equally specific registered message types.</param>
    /// <returns>The formatted exception message.</returns>
    private static string BuildMessage(
        Type messageType,
        Type resolveStrategyType,
        IReadOnlyList<Type> candidates)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(resolveStrategyType);
        ArgumentNullException.ThrowIfNull(candidates);

        var candidateNames = candidates.Count == 0
            ? "(none)"
            : string.Join(", ", candidates.Select(candidate => $"'{candidate.FullName ?? candidate.Name}'"));

        return
            $"More than one message descriptor is assignable from '{messageType.FullName ?? messageType.Name}' " +
            $"using resolve strategy '{resolveStrategyType.FullName ?? resolveStrategyType.Name}'. " +
            $"Ambiguous candidates: {candidateNames}. " +
            "Register handlers for a single most-derived message type or use a more specific resolve strategy.";
    }
}
