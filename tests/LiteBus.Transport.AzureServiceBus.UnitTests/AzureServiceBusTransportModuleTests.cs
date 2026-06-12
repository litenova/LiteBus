using AwesomeAssertions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;
using LiteBus.Transport;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.AzureServiceBus.UnitTests;

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

        var act = () => module.Build(configuration);

        act.Should().Throw<TransportAlreadyRegisteredException>();
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
}
