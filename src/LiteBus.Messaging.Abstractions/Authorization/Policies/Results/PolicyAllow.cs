namespace LiteBus.Messaging.Abstractions.Authorization;

/// <summary>
///     The policy result that permits the message to proceed.
/// </summary>
/// <remarks>
///     Returned as the shared <see cref="PolicyResult.Allow" /> value, so permitting an evaluation allocates nothing.
/// </remarks>
public sealed record PolicyAllow : PolicyResult
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PolicyAllow" /> class.
    /// </summary>
    internal PolicyAllow()
    {
    }
}
