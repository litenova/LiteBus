using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Thrown when <see cref="IHandleContextData.Get{T}" /> is asked for a type no stage stored.
/// </summary>
/// <remarks>
///     The message names the type, because the usual cause is a stage that was expected to supply the value and did
///     not: a guard that returned early, a pre-handler filtered out by a mediation tag, or a value stored under a
///     derived type and read back as its interface. Call <see cref="IHandleContextData.TryGet{T}" /> instead where the
///     value is genuinely optional.
/// </remarks>
public sealed class HandleContextDataNotFoundException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="HandleContextDataNotFoundException" /> class.
    /// </summary>
    /// <param name="dataType">The type that was requested and not found.</param>
    public HandleContextDataNotFoundException(Type dataType)
        : base(BuildMessage(dataType))
    {
        DataType = dataType;
    }

    /// <summary>
    ///     Gets the type that was requested and not found.
    /// </summary>
    public Type DataType { get; }

    /// <summary>
    ///     Builds the exception message, naming the requested type and the contract that supplies it.
    /// </summary>
    /// <param name="dataType">The type that was requested and not found.</param>
    /// <returns>The exception message.</returns>
    private static string BuildMessage(Type dataType)
    {
        ArgumentNullException.ThrowIfNull(dataType);

        return $"No value of type '{dataType.Name}' is present in the execution context data. An earlier pipeline stage "
               + "has to store it with IExecutionContext.Data.Set, and that stage has to run for this message. Use "
               + "TryGet when the value is optional.";
    }
}
