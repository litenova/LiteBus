using System;
using Azure.Storage.Blobs;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Auditing.Anchors.AzureBlob;

/// <summary>
///     Module that registers the Azure Blob checkpoint anchor settings, client, and connectivity probe.
/// </summary>
/// <remarks>
///     The auditing module owns the checkpoint lifecycle and resolves the publisher and source types recorded through
///     <c>AuditHashChainBuilder.PublishCheckpointsToAzureBlob</c>. That extension registers this module as a child of
///     the auditing module, so the container constructs the publisher and source with their settings and client.
///     Applications needing credentials other than a connection string construct <see cref="BlobServiceClient" />
///     themselves and register it with the application container, which resolves their client instead of the one
///     this module registers.
/// </remarks>
public sealed class AzureBlobCheckpointAnchorModule : IModule
{
    /// <summary>
    ///     Gets the anchor settings configured by the application.
    /// </summary>
    private readonly AzureBlobCheckpointAnchorOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureBlobCheckpointAnchorModule" /> class.
    /// </summary>
    /// <param name="options">The anchor settings configured by the application.</param>
    public AzureBlobCheckpointAnchorModule(AzureBlobCheckpointAnchorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        AzureBlobCheckpointAnchorOptionsValidator.Validate(options);
        _options = options;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(AzureBlobCheckpointAnchorOptions),
            _options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(BlobServiceClient),
            static serviceProvider =>
            {
                var options = serviceProvider.GetService(typeof(AzureBlobCheckpointAnchorOptions))
                                  as AzureBlobCheckpointAnchorOptions ??
                              throw new InvalidOperationException($"{nameof(AzureBlobCheckpointAnchorOptions)} is not registered.");

                return new BlobServiceClient(options.ConnectionString);
            },
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(AzureBlobCheckpointAnchorDiagnosticCheck),
            typeof(AzureBlobCheckpointAnchorDiagnosticCheck),
            InstanceLifetime.Singleton));

        configuration.RegisterDiagnosticCheck(
            typeof(AzureBlobCheckpointAnchorDiagnosticCheck),
            AzureBlobCheckpointAnchorDiagnosticCheck.ProbeName);
    }
}
