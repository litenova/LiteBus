using System.Collections.Generic;

namespace LiteBus.Commands.Abstractions;

/// <summary>
/// Represents a transactional batch of commands retrieved from the inbox for processing.
/// This collection should be treated as a single, atomic unit of work.
/// </summary>
public interface ICommandInboxBatch : IReadOnlyCollection<ICommand>;