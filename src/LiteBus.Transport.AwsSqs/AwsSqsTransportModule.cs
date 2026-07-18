using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using LiteBus.Runtime.Abstractions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.AwsSqs;

/// <summary>
///     Module that registers AWS SQS transport services implementing <see cref="Abstractions.ITransportPublisher" />.
/// </summary>
public sealed class AwsSqsTransportModule : IModule
{
    /// <summary>
    ///     Gets the connection settings configured by the application.
    /// </summary>
    private readonly AwsSqsTransportOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AwsSqsTransportModule" /> class.
    /// </summary>
    /// <param name="options">The connection settings configured by the application.</param>
    public AwsSqsTransportModule(AwsSqsTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        _options = options;
    }

    /// <summary>
    ///     Validates SQS credentials, polling, visibility, and retry settings before module composition.
    /// </summary>
    /// <param name="options">The transport settings to validate.</param>
    private static void ValidateOptions(AwsSqsTransportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AccessKey) != string.IsNullOrWhiteSpace(options.SecretKey))
        {
            throw new ArgumentException("AccessKey and SecretKey must either both be supplied or both be omitted.", nameof(options));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(options.LongPollWaitTimeSeconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.LongPollWaitTimeSeconds, 20);
        ArgumentOutOfRangeException.ThrowIfNegative(options.VisibilityTimeoutSeconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.VisibilityTimeoutSeconds, 43_200);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.RequeueVisibilityTimeoutSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.RequeueVisibilityTimeoutSeconds, 43_200);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            options.MaxRequeueVisibilityTimeoutSeconds,
            options.RequeueVisibilityTimeoutSeconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxRequeueVisibilityTimeoutSeconds, 43_200);
        ValidateMultiplier(options.RequeueBackoffMultiplier, nameof(options.RequeueBackoffMultiplier));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.PollBackoffInitial, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.PollBackoffMax, options.PollBackoffInitial);
        ValidateMultiplier(options.PollBackoffMultiplier, nameof(options.PollBackoffMultiplier));
    }

    /// <summary>
    ///     Validates one exponential backoff multiplier.
    /// </summary>
    /// <param name="multiplier">The multiplier to validate.</param>
    /// <param name="parameterName">The public option name reported on failure.</param>
    private static void ValidateMultiplier(double multiplier, string parameterName)
    {
        if (!double.IsFinite(multiplier) || multiplier < 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, multiplier, "Backoff multipliers must be finite and at least one.");
        }
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(AwsSqsTransportOptions),
            _options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAmazonSQS),
            static serviceProvider =>
            {
                var options = serviceProvider.GetService(typeof(AwsSqsTransportOptions))
                                  as AwsSqsTransportOptions ??
                              throw new InvalidOperationException($"{nameof(AwsSqsTransportOptions)} is not registered.");

                var config = new AmazonSQSConfig();

                if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
                {
                    config.ServiceURL = options.ServiceUrl;
                }
                else if (!string.IsNullOrWhiteSpace(options.Region))
                {
                    config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
                }

                if (!string.IsNullOrWhiteSpace(options.AccessKey) && !string.IsNullOrWhiteSpace(options.SecretKey))
                {
                    var credentials = new BasicAWSCredentials(options.AccessKey, options.SecretKey);
                    return new AmazonSQSClient(credentials, config);
                }

                return new AmazonSQSClient(config);
            },
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ITransportCircuitBreakerRegistry),
            static _ => new TransportCircuitBreakerRegistry(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ITransportPublisher),
            typeof(SqsPublisher)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageConsumer),
            typeof(SqsConsumer)));

        TransportMetricsRegistration.RegisterIfNeeded(configuration, "sqs");

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(AwsSqsConnectivityDiagnosticCheck),
            typeof(AwsSqsConnectivityDiagnosticCheck),
            InstanceLifetime.Singleton));

        configuration.RegisterDiagnosticCheck(
            typeof(AwsSqsConnectivityDiagnosticCheck),
            "transport.sqs.connectivity");
    }
}
