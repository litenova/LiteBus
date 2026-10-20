using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Resolves contract lookup using the declared message type instead of the runtime instance type.
/// </summary>
public sealed class DeclaredTypeMessageContractResolver : IMessageContractResolver
{
    /// <inheritdoc />
    public Type ResolveContractType(Type declaredType, object message)
    {
        ArgumentNullException.ThrowIfNull(declaredType);
        ArgumentNullException.ThrowIfNull(message);
        return declaredType;
    }
}
