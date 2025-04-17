using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a read-only collection of lazily initialized handlers with their associated descriptors.
/// </summary>
/// <typeparam name="THandler">The type of the handlers in the collection.</typeparam>
/// <typeparam name="TDescriptor">The type of the handler descriptors in the collection.</typeparam>
/// <remarks>
///     This interface extends <see cref="IReadOnlyCollection{T}" /> to provide a collection of
///     <see cref="LazyHandler{THandler, TDescriptor}" /> instances. The lazy initialization pattern
///     allows for efficient handling of large numbers of handlers by creating handler instances
///     only when they are actually needed.
/// </remarks>
public interface ILazyHandlerCollection<THandler, TDescriptor> : IReadOnlyCollection<LazyHandler<THandler, TDescriptor>>
    where TDescriptor : IHandlerDescriptor;