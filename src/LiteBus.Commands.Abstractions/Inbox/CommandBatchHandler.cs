using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents the method that will handle a batch of commands processed from the inbox.
/// </summary>
/// <param name="commandInboxBatch">A collection of commands to be processed as a single unit of work.</param>
/// <param name="cancellationToken">A token to signal that processing should be stopped.</param>
/// <remarks>
///     If this handler throws an exception, the underlying <see cref="ICommandInboxProcessor" />
///     implementation is responsible for catching it and handling the batch's retry or dead-lettering policy.
/// </remarks>
public delegate Task CommandBatchHandler(
    ICommandInboxBatch commandInboxBatch,
    CancellationToken cancellationToken = default);