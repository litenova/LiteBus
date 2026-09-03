using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Thrown when <see cref="IHandleContextData.Get{T}()" /> is asked for a slot no stage stored.
/// </summary>
/// <remarks>
///     The message names the type, and the key when the read was keyed, because the usual cause is a stage that was
///     expected to supply the value and did not: a guard that returned early, a pre-handler filtered out by a mediation
///     tag, a value stored under a derived type and read back as its interface, or a keyed read against a value that
///     was stored unkeyed. Call <see cref="IHandleContextData.TryGet{T}(out T)" /> instead where the value is genuinely
///     optional.
/// </remarks>
public sealed class HandleContextDataNotFoundException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="HandleContextDataNotFoundException" /> class.
    /// </summary>
    /// <param name="dataType">The type that was requested and not found.</param>
    /// <param name="key">The key the read used, or <see langword="null" /> when the read was unkeyed.</param>
    public HandleContextDataNotFoundException(Type dataType, object? key = null)
        : base(BuildMessage(dataType, key))
    {
        DataType = dataType;
        Key = key;
    }

    /// <summary>
    ///     Gets the type that was requested and not found.
    /// </summary>
    public Type DataType { get; }

    /// <summary>
    ///     Gets the key the read used.
    /// </summary>
    /// <value>The caller-supplied key, or <see langword="null" /> when the read addressed the unkeyed slot.</value>
    public object? Key { get; }

    /// <summary>
    ///     Builds the exception message, naming the requested slot and the contract that supplies it.
    /// </summary>
    /// <param name="dataType">The type that was requested and not found.</param>
    /// <param name="key">The key the read used, or <see langword="null" /> when the read was unkeyed.</param>
    /// <returns>The exception message.</returns>
    private static string BuildMessage(Type dataType, object? key)
    {
        ArgumentNullException.ThrowIfNull(dataType);

        var slot = key is null
            ? $"No value of type '{dataType.Name}'"
            : $"No value of type '{dataType.Name}' under key '{key}'";

        return $"{slot} is present in the execution context data. An earlier pipeline stage has to store it with "
               + "IExecutionContext.Data.Set, and that stage has to run for this message. A keyed value and an unkeyed "
               + "value of the same type are separate slots, so both ends have to agree on which one they use. Use "
               + "TryGet when the value is optional.";
    }
}
