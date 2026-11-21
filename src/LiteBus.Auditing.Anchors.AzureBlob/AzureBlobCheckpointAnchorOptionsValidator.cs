using System;
using Azure.Storage.Blobs;

namespace LiteBus.Auditing.Anchors.AzureBlob;

/// <summary>
///     Validates Azure Blob checkpoint anchor settings before module composition.
/// </summary>
internal static class AzureBlobCheckpointAnchorOptionsValidator
{
    /// <summary>
    ///     The shortest container name Azure Storage accepts.
    /// </summary>
    private const int MinContainerNameLength = 3;

    /// <summary>
    ///     The longest container name Azure Storage accepts.
    /// </summary>
    private const int MaxContainerNameLength = 63;

    /// <summary>
    ///     Validates anchor settings and reports the first problem found.
    /// </summary>
    /// <param name="options">The anchor settings to validate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when the connection string is blank or malformed, the container name breaks Azure naming rules, or
    ///     the blob name prefix starts with a slash.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when the immutability retention is set without being positive.
    /// </exception>
    public static void Validate(AzureBlobCheckpointAnchorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ContainerName);

        if (!IsValidContainerName(options.ContainerName))
        {
            throw new ArgumentException(
                "The container name must be 3 to 63 characters of lowercase letters, numbers, and single dashes, " +
                "starting and ending with a letter or number.",
                nameof(options));
        }

        ArgumentNullException.ThrowIfNull(options.BlobNamePrefix);

        if (options.BlobNamePrefix.StartsWith('/'))
        {
            throw new ArgumentException(
                "The blob name prefix must be relative to the container root and must not start with a slash.",
                nameof(options));
        }

        if (options.BlobNamePrefix.Length != 0 && string.IsNullOrWhiteSpace(options.BlobNamePrefix))
        {
            throw new ArgumentException(
                "The blob name prefix must be empty or hold a path.",
                nameof(options));
        }

        if (options.ImmutabilityRetention is { } retention)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero);
        }

        try
        {
            _ = new BlobServiceClient(options.ConnectionString);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "The connection string is not a valid Azure Storage connection string.",
                nameof(options),
                exception);
        }
    }

    /// <summary>
    ///     Determines whether a container name follows the Azure Storage naming rules.
    /// </summary>
    /// <param name="name">The container name to check.</param>
    /// <returns><see langword="true" /> when the name is acceptable; otherwise, <see langword="false" />.</returns>
    private static bool IsValidContainerName(string name)
    {
        if (name.Length is < MinContainerNameLength or > MaxContainerNameLength)
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

            if (character != '-')
            {
                return false;
            }

            if (index == 0 || index == name.Length - 1)
            {
                return false;
            }

            if (name[index - 1] == '-')
            {
                return false;
            }
        }

        return true;
    }
}
