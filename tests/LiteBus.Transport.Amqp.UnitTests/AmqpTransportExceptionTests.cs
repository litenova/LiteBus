using LiteBus.Transport.Amqp.Exceptions;

namespace LiteBus.Transport.Amqp.UnitTests;

public sealed class AmqpTransportExceptionTests
{
    [Fact]
    public void AmqpTransportConfigurationException_exposes_message()
    {
        var exception = new AmqpTransportConfigurationException("The AMQP consumer is already started.");

        exception.Message.Should().Be("The AMQP consumer is already started.");
    }
}