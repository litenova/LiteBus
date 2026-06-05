using System.Reflection;
using LiteBus.Saga.Abstractions;

namespace LiteBus.Saga;

/// <summary>
///     Invokes generic <see cref="ISagaStore" /> methods for runtime-resolved state types.
/// </summary>
internal static class SagaStoreInvoker
{
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
        var method = typeof(ISagaStore)
            .GetMethod(nameof(ISagaStore.LoadAsync), BindingFlags.Public | BindingFlags.Instance)!
            .MakeGenericMethod(stateType);

        var task = (Task)method.Invoke(store, [correlation, cancellationToken])!;
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
            (int)versionProperty!.GetValue(result)!,
            (bool)completedProperty!.GetValue(result)!);
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
        var method = typeof(ISagaStore)
            .GetMethod(nameof(ISagaStore.SaveAsync), BindingFlags.Public | BindingFlags.Instance)!
            .MakeGenericMethod(stateType);

        return (Task)method.Invoke(store, [correlation, state, expectedVersion, cancellationToken])!;
    }
}
