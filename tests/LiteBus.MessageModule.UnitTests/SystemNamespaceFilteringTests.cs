using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Testing;

namespace LiteBus.MessageModule.UnitTests
{
    [Collection("Sequential")]
    public sealed class SystemNamespaceFilteringTests : LiteBusTestBase
    {
        [Fact]
        public void Register_MessageInNamespaceStartingWithSystem_ShouldRegisterAsMessage()
        {
            var registry = new MessageRegistry();

            registry.Register(typeof(Systematic.Domain.Events.SystematicEvent));

            registry.Should().HaveCount(1);
            registry.First().MessageType.Should().Be(typeof(Systematic.Domain.Events.SystematicEvent));
        }

        [Fact]
        public void Register_SystemNamespaceType_ShouldNotRegisterAsMessage()
        {
            var registry = new MessageRegistry();

            registry.Register(typeof(Uri));

            registry.Register(typeof(DateTimeOffset));

            registry.Register(typeof(System.Collections.Generic.List<string>));

            registry.Should().BeEmpty();
        }
    }
}

namespace Systematic.Domain.Events
{
    public sealed record SystematicEvent : IEvent;
}