using System;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using LiteBus.Auditing;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Auditing.Anchors.S3;

/// <summary>
///     Module that registers the S3 checkpoint anchor settings, client, and connectivity probe.
/// </summary>
/// <remarks>
///     The auditing module owns the checkpoint lifecycle and resolves the publisher and source types recorded through
///     <c>AuditHashChainBuilder.PublishCheckpointsToS3</c>. That extension registers this module as a child of the
///     auditing module, so the container constructs the publisher and source with their settings and client.
///     Applications needing credentials other than an access key pair construct <see cref="IAmazonS3" />
///     themselves and register it with the application container, which resolves their client instead of the one
///     this module registers.
/// </remarks>
public sealed class S3CheckpointAnchorModule : IModule
{
    /// <summary>
    ///     Gets the anchor settings configured by the application.
    /// </summary>
    private readonly S3CheckpointAnchorOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="S3CheckpointAnchorModule" /> class.
    /// </summary>
    /// <param name="options">The anchor settings configured by the application.</param>
    public S3CheckpointAnchorModule(S3CheckpointAnchorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        S3CheckpointAnchorOptionsValidator.Validate(options);
        _options = options;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(S3CheckpointAnchorOptions),
            _options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAmazonS3),
            static serviceProvider =>
            {
                var options = serviceProvider.GetService(typeof(S3CheckpointAnchorOptions))
                                  as S3CheckpointAnchorOptions ??
                              throw new InvalidOperationException($"{nameof(S3CheckpointAnchorOptions)} is not registered.");

                var config = new AmazonS3Config
                {
                    ForcePathStyle = options.ForcePathStyle,
                };

                if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
                {
                    config.ServiceURL = options.ServiceUrl;
                    config.UseHttp = options.ServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
                }
                else if (!string.IsNullOrWhiteSpace(options.Region))
                {
                    config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
                }

                try
                {
                    if (!string.IsNullOrWhiteSpace(options.AccessKey) && !string.IsNullOrWhiteSpace(options.SecretKey))
                    {
                        var credentials = new BasicAWSCredentials(options.AccessKey, options.SecretKey);
                        return new AmazonS3Client(credentials, config);
                    }

                    return new AmazonS3Client(config);
                }
                catch (AmazonClientException exception)
                {
                    throw new S3CheckpointAnchorException(
                        "The S3 client could not be created from the configured options. When neither " +
                        "ServiceUrl nor Region is set, the AWS SDK resolves the region from environment " +
                        "variables, the shared configuration file, or instance metadata.",
                        exception);
                }
            },
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(S3CheckpointAnchorDiagnosticCheck),
            typeof(S3CheckpointAnchorDiagnosticCheck),
            InstanceLifetime.Singleton));

        configuration.RegisterDiagnosticCheck(
            typeof(S3CheckpointAnchorDiagnosticCheck),
            S3CheckpointAnchorDiagnosticCheck.ProbeName);
    }
}
