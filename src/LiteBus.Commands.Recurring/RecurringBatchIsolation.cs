namespace LiteBus.Commands.Recurring;

/// <summary>
///     Chooses how one pass of a recurring batch loop sends its items.
/// </summary>
/// <remarks>
///     The default is <see cref="Atomic" />: the whole batch commits or rolls back as one, exactly as a batch sent
///     through <see cref="LiteBus.Commands.Abstractions.ICommandBatchMediator.TrySendManyAsync" />. Choose
///     <see cref="PerItem" /> only when the items carry different identities or when one failing item must not roll
///     back the rest.
/// </remarks>
public enum RecurringBatchIsolation
{
    /// <summary>
    ///     The pass sends every item as one batch: the commands run one at a time in list order inside one dispatch
    ///     scope with one unit-of-work commit, and the first denied, invalid, faulted, or cancelled command rolls the
    ///     whole batch back.
    /// </summary>
    Atomic = 0,

    /// <summary>
    ///     The pass sends each item as its own mediation with its own identity: one failing or refused item is logged
    ///     and counted and never stops the rest of the batch.
    /// </summary>
    PerItem = 1
}
