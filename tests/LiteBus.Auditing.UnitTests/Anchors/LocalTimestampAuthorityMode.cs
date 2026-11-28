namespace LiteBus.Auditing.UnitTests.Anchors;

/// <summary>
///     How a <see cref="LocalTimestampAuthorityHandler" /> answers timestamp queries.
/// </summary>
internal enum LocalTimestampAuthorityMode
{
    /// <summary>
    ///     Signs the request imprint and returns a granted timestamp response.
    /// </summary>
    Success = 0,

    /// <summary>
    ///     Returns a timestamp response with a rejection status and no token.
    /// </summary>
    FailureStatus = 1,

    /// <summary>
    ///     Answers with an HTTP error status and no timestamp response.
    /// </summary>
    HttpError = 2,

    /// <summary>
    ///     Throws as if the authority cannot be reached.
    /// </summary>
    Unreachable = 3,

    /// <summary>
    ///     Waits until the request is canceled, so the signer timeout fires.
    /// </summary>
    NeverRespond = 4,
}
