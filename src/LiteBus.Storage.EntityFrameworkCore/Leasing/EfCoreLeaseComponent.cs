namespace LiteBus.Storage.EntityFrameworkCore.Leasing;

/// <summary>
///     Identifies which LiteBus store table shape raw lease SQL should target.
/// </summary>
internal enum EfCoreLeaseComponent
{
    /// <summary>
    ///     The inbox command table.
    /// </summary>
    Inbox = 0,

    /// <summary>
    ///     The outbox message table.
    /// </summary>
    Outbox = 1
}