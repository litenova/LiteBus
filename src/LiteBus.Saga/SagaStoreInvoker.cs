using System.Collections.Concurrent;
using System.Reflection;
using LiteBus.Saga.Abstractions;

namespace LiteBus.Saga;

/// <summary>
///     Invokes generic <see cref="ISagaStore" /> methods for runtime-resolved state types.
/// </summary>
internal static class SagaStoreInvoker
{
    /// <summary>
    ///     Cached generic invokers keyed by state type and operation.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type StateType, string Operation), object> InvokerCache = new();

    /// <summary>
    ///     Loads saga state for a runtime-resolved state type.
    /// </summary>
    /// <param name="store">The saga store.</param>
    /// <param name="stateType">The saga state type.</param>
    /// <param name="correlation">The saga correlation.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The loaded state, version, and completion flag when a row exists.</returns>
    internal static async Task<(object State, int Version, bool IsCompleted)?> LoadAsync(
        ISagaStore store,
        Type stateType,
        SagaCorrelation correlation,
        CancellationToken cancellationToken)
    {
        var invoker = (LoadInvoker) InvokerCache.GetOrAdd(
            (stateType, nameof(ISagaStore.LoadAsync)),
            _ => CreateLoadInvoker(stateType));

        return await invoker(store, correlation, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Saves saga state for a runtime-resolved state type.
    /// </summary>
    /// <param name="store">The saga store.</param>
    /// <param name="stateType">The saga state type.</param>
    /// <param name="correlation">The saga correlation.</param>
    /// <param name="state">The state object.</param>
    /// <param name="expectedVersion">The optimistic lock version.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when the save succeeds.</returns>
    internal static Task SaveAsync(
        ISagaStore store,
        Type stateType,
        SagaCorrelation correlation,
        object state,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var invoker = (SaveInvoker) InvokerCache.GetOrAdd(
            (stateType, nameof(ISagaStore.SaveAsync)),
            _ => CreateSaveInvoker(stateType));

        return invoker(store, correlation, state, expectedVersion, cancellationToken);
    }

    /// <summary>
    ///     Creates a cached load invoker for one state type.
    /// </summary>
    /// <param name="stateType">The saga state type.</param>
    /// <returns>The load invoker delegate.</returns>
    private static LoadInvoker CreateLoadInvoker(Type stateType)
    {
        var method = typeof(ISagaStore)
            .GetMethod(nameof(ISagaStore.LoadAsync), BindingFlags.Public | BindingFlags.Instance)!
            .MakeGenericMethod(stateType);

        return async (store, correlation, cancellationToken) =>
        {
            var task = (Task) method.Invoke(store, [correlation, cancellationToken])!;
            await task.ConfigureAwait(false);

            var result = task.GetType().GetProperty("Result")?.GetValue(task);

            if (result is null)
            {
                return null;
            }

            var stateProperty = result.GetType().GetProperty(nameof(SagaInstance<object>.State));
            var versionProperty = result.GetType().GetProperty(nameof(SagaInstance<object>.Version));
            var completedProperty = result.GetType().GetProperty(nameof(SagaInstance<object>.IsCompleted));

            return (
                stateProperty!.GetValue(result)!,
                (int) versionProperty!.GetValue(result)!,
                (bool) completedProperty!.GetValue(result)!);
        };
    }

    /// <summary>
    ///     Creates a cached save invoker for one state type.
    /// </summary>
    /// <param name="stateType">The saga state type.</param>
    /// <returns>The save invoker delegate.</returns>
    private static SaveInvoker CreateSaveInvoker(Type stateType)
    {
        var method = typeof(ISagaStore)
            .GetMethod(nameof(ISagaStore.SaveAsync), BindingFlags.Public | BindingFlags.Instance)!
            .MakeGenericMethod(stateType);

        var itemType = typeof(SagaSaveItem<>).MakeGenericType(stateType);

        return (store, correlation, state, expectedVersion, cancellationToken) =>
        {
            var item = Activator.CreateInstance(itemType, correlation, state, expectedVersion)!;
            return (Task) method.Invoke(store, [item, cancellationToken])!;
        };
    }

    /// <summary>
    ///     Delegate that loads saga state for one runtime-resolved state type.
    /// </summary>
    /// <param name="store">The saga store.</param>
    /// <param name="correlation">The saga correlation.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The loaded state tuple when a row exists.</returns>
    private delegate Task<(object State, int Version, bool IsCompleted)?> LoadInvoker(
        ISagaStore store,
        SagaCorrelation correlation,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Delegate that saves saga state for one runtime-resolved state type.
    /// </summary>
    /// <param name="store">The saga store.</param>
    /// <param name="correlation">The saga correlation.</param>
    /// <param name="state">The state object.</param>
    /// <param name="expectedVersion">The optimistic lock version.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when the save succeeds.</returns>
    private delegate Task SaveInvoker(
        ISagaStore store,
        SagaCorrelation correlation,
        object state,
        int expectedVersion,
        CancellationToken cancellationToken);
}
