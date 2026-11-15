using System;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Carries the host options of one recurring command batch, registered once per source type.
/// </summary>
/// <typeparam name="TSource">The source that produces the commands of each pass.</typeparam>
/// <remarks>
///     <see cref="CommandModuleBuilderRecurringExtensions.AddRecurringBatch{TSource}" /> registers one declaration per
///     source as a singleton instance, and <see cref="RecurringCommandBatchBackgroundService{TSource}" /> takes it
///     through its constructor. Keying the options by the source type lets several recurring batches keep their own
///     options, and the host can construct the background service from its constructor alone.
/// </remarks>
public sealed class RecurringCommandBatchDeclaration<TSource>
    where TSource : class, IRecurringCommandBatchSource
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RecurringCommandBatchDeclaration{TSource}" /> class.
    /// </summary>
    /// <param name="hostOptions">The lifecycle and timing settings of the loop.</param>
    public RecurringCommandBatchDeclaration(RecurringCommandHostOptions hostOptions)
    {
        ArgumentNullException.ThrowIfNull(hostOptions);
        HostOptions = hostOptions;
    }

    /// <summary>
    ///     Gets the lifecycle and timing settings of the loop.
    /// </summary>
    public RecurringCommandHostOptions HostOptions { get; }
}
