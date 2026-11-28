using System;

namespace LiteBus.Auditing.Anchors.S3;

/// <summary>
///     Validates S3 checkpoint anchor settings before module composition.
/// </summary>
internal static class S3CheckpointAnchorOptionsValidator
{
    /// <summary>
    ///     The shortest bucket name S3-compatible stores accept.
    /// </summary>
    private const int MinBucketNameLength = 3;

    /// <summary>
    ///     The longest bucket name S3-compatible stores accept.
    /// </summary>
    private const int MaxBucketNameLength = 63;

    /// <summary>
    ///     Validates anchor settings and reports the first problem found.
    /// </summary>
    /// <param name="options">The anchor settings to validate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when the bucket name is blank or breaks S3 naming rules, the key prefix starts with a slash, only
    ///     one of the credential pair is supplied, the service URL is not an absolute HTTP or HTTPS address, or a
    ///     retention mode is set without a retention period and vice versa.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when the retention mode is not defined or the retention period is set without being positive.
    /// </exception>
    public static void Validate(S3CheckpointAnchorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.BucketName);

        if (!IsValidBucketName(options.BucketName))
        {
            throw new ArgumentException(
                "The bucket name must be 3 to 63 characters of lowercase letters, numbers, dots, and single dashes, " +
                "starting and ending with a letter or number.",
                nameof(options));
        }

        ArgumentNullException.ThrowIfNull(options.KeyPrefix);

        if (options.KeyPrefix.StartsWith('/'))
        {
            throw new ArgumentException(
                "The key prefix must be relative to the bucket root and must not start with a slash.",
                nameof(options));
        }

        if (options.KeyPrefix.Length != 0 && string.IsNullOrWhiteSpace(options.KeyPrefix))
        {
            throw new ArgumentException(
                "The key prefix must be empty or hold a path.",
                nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.AccessKey) != string.IsNullOrWhiteSpace(options.SecretKey))
        {
            throw new ArgumentException(
                "AccessKey and SecretKey must either both be supplied or both be omitted.",
                nameof(options));
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl) && !IsHttpUrl(options.ServiceUrl))
        {
            throw new ArgumentException(
                "The service URL must be an absolute HTTP or HTTPS address.",
                nameof(options));
        }

        if (options.RetentionMode.HasValue && !Enum.IsDefined(options.RetentionMode.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The retention mode is not defined.");
        }

        if (options.RetentionMode.HasValue && !options.RetentionPeriod.HasValue)
        {
            throw new ArgumentException(
                "The retention mode requires a retention period.",
                nameof(options));
        }

        if (!options.RetentionMode.HasValue && options.RetentionPeriod.HasValue)
        {
            throw new ArgumentException(
                "The retention period requires a retention mode.",
                nameof(options));
        }

        if (options.RetentionPeriod is { } retention)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero);
        }
    }

    /// <summary>
    ///     Determines whether a bucket name follows the S3 naming rules shared by S3-compatible stores.
    /// </summary>
    /// <param name="name">The bucket name to check.</param>
    /// <returns><see langword="true" /> when the name is acceptable; otherwise, <see langword="false" />.</returns>
    private static bool IsValidBucketName(string name)
    {
        if (name.Length is < MinBucketNameLength or > MaxBucketNameLength)
        {
            return false;
        }

        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            var isLowerLetter = character is >= 'a' and <= 'z';
            var isDigit = character is >= '0' and <= '9';

            if (isLowerLetter || isDigit)
            {
                continue;
            }

            if (character is not '-' and not '.')
            {
                return false;
            }

            if (index == 0 || index == name.Length - 1)
            {
                return false;
            }

            if (name[index - 1] is '-' or '.')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Determines whether a service URL is an absolute HTTP or HTTPS address.
    /// </summary>
    /// <param name="serviceUrl">The service URL to check.</param>
    /// <returns><see langword="true" /> when the address is absolute HTTP or HTTPS; otherwise, <see langword="false" />.</returns>
    private static bool IsHttpUrl(string serviceUrl)
    {
        return Uri.TryCreate(serviceUrl, UriKind.Absolute, out var address) &&
            (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps);
    }
}
