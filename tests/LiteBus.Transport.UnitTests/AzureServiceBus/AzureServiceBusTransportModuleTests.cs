using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;
using LiteBus.Transport;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.AzureServiceBus;

namespace LiteBus.Transport.UnitTests.AzureServiceBus;

/// <summary>
///     Verifies Azure Service Bus transport module registration behavior.
/// </summary>
public sealed class AzureServiceBusTransportModuleTests
{
    /// <summary>
    ///     Verifies the module rejects duplicate transport registration.
    /// </summary>
    [Fact]
    public void Build_ShouldRejectDuplicateTransportRegistration()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        var options = new AzureServiceBusTransportOptions
        {
            ConnectionString = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=key"
        };

        var module = new AzureServiceBusTransportModule(options);

        module.Build(configuration);

        configuration.DiagnosticChecks.Should().ContainSingle(descriptor =>
            descriptor.ImplementationType == typeof(AzureServiceBusConnectivityDiagnosticCheck) &&
            descriptor.Name == "transport.azure_service_bus.connectivity");

        var act = () => module.Build(configuration);

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*already registered*");
    }

    /// <summary>
    ///     Verifies the module rejects empty connection strings at construction time.
    /// </summary>
    [Fact]
    public void Constructor_ShouldRejectEmptyConnectionString()
    {
        var act = () => new AzureServiceBusTransportModule(new AzureServiceBusTransportOptions
        {
            ConnectionString = "   "
        });

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Verifies an empty diagnostic queue name fails during module construction.
    /// </summary>
    [Fact]
    public void Constructor_WithEmptyDiagnosticQueueName_ShouldThrow()
    {
        var act = () => new AzureServiceBusTransportModule(new AzureServiceBusTransportOptions
        {
            ConnectionString = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=key",
            ConnectivityCheckTarget = new AzureServiceBusQueueDiagnosticTarget(" ")
        });

        act.Should().Throw<ArgumentException>();
    }
}
