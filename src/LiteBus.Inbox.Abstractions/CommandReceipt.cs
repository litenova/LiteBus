using System;
using LiteBus.Commands.Abstractions;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Represents the result of accepting a command into the inbox.
/// </summary>
/// <remarks>
///     <para>
///         A receipt means the store accepted the command envelope. It does not mean the command handler has run. Return
///         this value from an API as acceptance or tracking data, then use queries to read the state produced by later
///         command execution.
///     </para>
/// </remarks>
/// <typeparam name="TCommand">The command type associated with the receipt.</typeparam>
public sealed record CommandReceipt<TCommand>
    where TCommand : ICommand
{
    /// <summary>
    ///     Gets the unique command identifier that processors and tracking endpoints can use.
    /// </summary>
    public required Guid CommandId { get; init; }

    /// <summary>
    ///     Gets the CLR command type that was scheduled. For closed generic commands, this is the closed runtime type.
    /// </summary>
    public required Type CommandType { get; init; }

    /// <summary>
    ///     Gets the stable message contract name stored with the payload.
    /// </summary>
    public required string ContractName { get; init; }

    /// <summary>
    ///     Gets the contract version used when the command was serialized.
    /// </summary>
    public required int ContractVersion { get; init; }

    /// <summary>
    ///     Gets the UTC timestamp when the command was accepted by the store.
    /// </summary>
    public required DateTimeOffset AcceptedAt { get; init; }

    /// <summary>
    ///     Gets the optional correlation identifier copied from scheduling options or from the stored duplicate row.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    ///     Gets the optional causation identifier copied from scheduling options or from the stored duplicate row.
    /// </summary>
    public string? CausationId { get; init; }

    /// <summary>
    ///     Gets the optional tenant identifier copied from scheduling options or from the stored duplicate row.
    /// </summary>
    public string? TenantId { get; init; }
}