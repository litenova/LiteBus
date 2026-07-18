# LiteBus.Transport.Testing

`LiteBus.Transport.Testing` provides reusable xUnit contract tests for third-party LiteBus transport adapters.

Reference the package from an xUnit test project, derive a concrete class from `TransportContractTests`, and return an isolated `TransportContractContext` for each scenario. The context supplies the adapter publisher, consumer settings, publish destination, optional route, and one cleanup callback.

```csharp
public sealed class CustomTransportContractTests : TransportContractTests
{
    protected override async ValueTask<TransportContractContext> CreateContextAsync(string scenario)
    {
        var resources = await CustomTransportResources.StartAsync(scenario);

        return new TransportContractContext(
            resources.Publisher,
            resources.Consumer,
            new TransportConsumerOptions { Destination = resources.QueueName },
            resources.QueueName,
            resources.DisposeAsync);
    }
}
```

The inherited suite verifies payload and metadata round trips, explicit redelivery, and pre-publication cancellation. Run the suite against a real broker in CI; an in-memory substitute does not validate broker acknowledgement or header behavior.
