using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Auditing.Anchors.S3;

/// <summary>
///     Verifies S3 anchor reachability by reading the checkpoint bucket.
/// </summary>
public sealed class S3CheckpointAnchorDiagnosticCheck : IDiagnosticCheck
{
    /// <summary>
    ///     Gets the stable probe name reported to operators and health hosts.
    /// </summary>
    public const string ProbeName = "auditing.anchor.s3.connectivity";

    /// <summary>
    ///     Gets the shared S3 client used to read the checkpoint bucket.
    /// </summary>
    private readonly IAmazonS3 _client;

    /// <summary>
    ///     Gets the anchor settings naming the checkpoint bucket.
    /// </summary>
    private readonly S3CheckpointAnchorOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="S3CheckpointAnchorDiagnosticCheck" /> class.
    /// </summary>
    /// <param name="client">The shared S3 client used to read the checkpoint bucket.</param>
    /// <param name="options">The anchor settings naming the checkpoint bucket.</param>
    public S3CheckpointAnchorDiagnosticCheck(
        IAmazonS3 client,
        S3CheckpointAnchorOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        _client = client;
        _options = options;
    }

    /// <inheritdoc />
    public string Name => ProbeName;

    /// <inheritdoc />
    /// <remarks>
    ///     Reading the bucket changes nothing. A bucket that does not exist yet is degraded rather than
    ///     unhealthy, because the publisher creates it with the first checkpoint. The final
    ///     <see cref="Exception" /> handler prevents an adapter diagnostic from escaping into sibling probes.
    /// </remarks>
    public async Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.HeadBucketAsync(
                    new HeadBucketRequest { BucketName = _options.BucketName },
                    cancellationToken)
                .ConfigureAwait(false);

            return new DiagnosticResult(
                DiagnosticStatus.Healthy,
                "The S3 checkpoint bucket is available.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return CreateUnhealthyResult();
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return new DiagnosticResult(
                DiagnosticStatus.Degraded,
                "The S3 checkpoint bucket does not exist yet; it is created with the first published checkpoint.");
        }
        catch (AmazonS3Exception)
        {
            return CreateUnhealthyResult();
        }
#pragma warning disable CA1031 // Diagnostic probes must map unexpected failures to unhealthy results without throwing.
        catch (Exception)
#pragma warning restore CA1031
        {
            return CreateUnhealthyResult();
        }
    }

    /// <summary>
    ///     Creates an unhealthy result without exposing SDK exception text.
    /// </summary>
    /// <returns>The unhealthy diagnostic result.</returns>
    private static DiagnosticResult CreateUnhealthyResult()
    {
        return new DiagnosticResult(
            DiagnosticStatus.Unhealthy,
            "The S3 checkpoint bucket is unavailable.");
    }
}
