using System;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Signals from a pass to <see cref="RecurringCommandLoop" /> that the pass ended without doing its work, has
///     already logged why, and the loop should wait <see cref="RecurringCommandHostOptions.ErrorBackoff" />.
/// </summary>
/// <remarks>
///     A recurring batch with failed items, or a tenant sweep with failed tenants, raises it after logging each
///     failure, so a command refused on every pass waits the error backoff instead of spinning, and the loop does not
///     log the failures a second time as faults.
/// </remarks>
internal sealed class RecurringPassBackoffException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RecurringPassBackoffException" /> class.
    /// </summary>
    public RecurringPassBackoffException()
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecurringPassBackoffException" /> class.
    /// </summary>
    /// <param name="message">The reason the pass backed off.</param>
    public RecurringPassBackoffException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecurringPassBackoffException" /> class.
    /// </summary>
    /// <param name="message">The reason the pass backed off.</param>
    /// <param name="innerException">The exception that ended the pass.</param>
    public RecurringPassBackoffException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
