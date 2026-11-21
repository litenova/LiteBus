using System.Collections.Generic;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Describes a command batch to its participants when it starts.
/// </summary>
/// <param name="BatchId">The identifier of the batch, unique per <see cref="ICommandBatchMediator" /> call.</param>
/// <param name="CommandItems">
///     The items copied into the execution context items of every command in the batch. A participant adds the values
///     its pipeline handlers look up by key; the batch adds them after the caller's items, so a participant's key wins.
/// </param>
public sealed record CommandBatchStart(string BatchId, IDictionary<string, object> CommandItems);
