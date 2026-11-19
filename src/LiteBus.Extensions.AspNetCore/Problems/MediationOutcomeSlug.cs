using LiteBus.Messaging.Abstractions;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Maps a mediation outcome to the lowercase slug used in outcome-derived problem types.
/// </summary>
internal static class MediationOutcomeSlug
{
    /// <summary>
    ///     Returns the slug for an outcome, such as <c>invalid</c> for <see cref="MediationOutcome.Invalid" />.
    /// </summary>
    /// <param name="outcome">How the mediation ended.</param>
    /// <returns>The lowercase outcome slug.</returns>
    internal static string ToSlug(MediationOutcome outcome)
    {
        return outcome switch
        {
            MediationOutcome.Succeeded => "succeeded",
            MediationOutcome.Answered => "answered",
            MediationOutcome.Denied => "denied",
            MediationOutcome.Failed => "failed",
            MediationOutcome.Canceled => "canceled",
            MediationOutcome.Invalid => "invalid",
            _ => outcome.ToString("G"),
        };
    }
}
