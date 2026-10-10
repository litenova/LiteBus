using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     Resolves the result type a message declares through <see cref="IProducesResult{TMessageResult}" />.
/// </summary>
/// <remarks>
///     Shared by the registry, which needs the answer to close open generic handlers, and the pipeline explainer,
///     which reports it, so the two cannot disagree about what a message produces.
/// </remarks>
internal static class DeclaredResultTypeResolver
{
    /// <summary>
    ///     Resolves the result type a message declares through <see cref="IProducesResult{TMessageResult}" />.
    /// </summary>
    /// <param name="messageType">The concrete message type.</param>
    /// <returns>The declared result type, or <see langword="null" /> when the message declares none or declares several.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="messageType" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     Read from the message's own contract rather than from a main handler descriptor, so the answer does not
    ///     depend on whether the handler has been registered yet. A message declaring two result types is a modelling
    ///     error the axis contracts already make hard to write, and there is no correct arity-2 closing for it, so it
    ///     is answered as no result and the handler is skipped rather than closed over an arbitrary choice.
    /// </remarks>
    public static Type? Find(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        Type? resultType = null;

        foreach (var contract in messageType.GetInterfaces())
        {
            if (!contract.IsGenericType || contract.GetGenericTypeDefinition() != typeof(IProducesResult<>))
            {
                continue;
            }

            if (resultType is not null)
            {
                return null;
            }

            resultType = contract.GetGenericArguments()[0];
        }

        return resultType;
    }
}
