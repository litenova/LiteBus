using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.AwsSqs;
using LiteBus.Transport.InMemory;

namespace LiteBus.Transport.UnitTests.AwsSqs;

/// <summary>
///     Verifies AWS SQS transport module registration behavior.
/// </summary>
public sealed class AwsSqsTransportModuleTests
{
    /// <summary>
    ///     Verifies the module registers transport services on first build.
    /// </summary>
    [Fact]
    public void Build_ShouldRegisterTransportServices()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        var options = new AwsSqsTransportOptions
        {
            Region = "us-east-1"
        };

        new AwsSqsTransportModule(options).Build(configuration);

        configuration.DependencyRegistry
            .Count(descriptor => descriptor.DependencyType == typeof(ITransportPublisher))
            .Should()
            .Be(1);

        configuration.DiagnosticChecks.Should().ContainSingle(descriptor =>
            descriptor.ImplementationType == typeof(AwsSqsConnectivityDiagnosticCheck) &&
            descriptor.Name == "transport.sqs.connectivity");
    }

    /// <summary>
    ///     Verifies a second transport module throws instead of silently no-oping.
    /// </summary>
    [Fact]
    public void Build_SecondTransportModule_ShouldThrow()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        new InMemoryTransportModule().Build(configuration);

        var options = new AwsSqsTransportOptions { Region = "us-east-1" };

        var act = () => new AwsSqsTransportModule(options).Build(configuration);

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*already registered*");
    }

    /// <summary>
    ///     Verifies unsafe polling, visibility, credential, and backoff settings fail during module construction.
    /// </summary>
    [Fact]
    public void Constructor_WithUnsafeConsumerOptions_ShouldThrow()
    {
        Action[] actions =
        [
            () => _ = new AwsSqsTransportModule(new AwsSqsTransportOptions { LongPollWaitTimeSeconds = 21 }),
            () => _ = new AwsSqsTransportModule(new AwsSqsTransportOptions { VisibilityTimeoutSeconds = -1 }),
            () => _ = new AwsSqsTransportModule(new AwsSqsTransportOptions { AccessKey = "access" }),
            () => _ = new AwsSqsTransportModule(new AwsSqsTransportOptions { PollBackoffInitial = TimeSpan.Zero }),
            () => _ = new AwsSqsTransportModule(new AwsSqsTransportOptions { PollBackoffMultiplier = double.NaN }),
            () => _ = new AwsSqsTransportModule(new AwsSqsTransportOptions
            {
                RequeueVisibilityTimeoutSeconds = 60,
                MaxRequeueVisibilityTimeoutSeconds = 30
            })
        ];

        foreach (var action in actions)
        {
            action.Should().Throw<ArgumentException>();
        }
    }
}
