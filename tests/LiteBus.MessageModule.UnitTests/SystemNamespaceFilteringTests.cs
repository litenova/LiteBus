using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Testing;
using Systematic.Domain.Events;

namespace LiteBus.MessageModule.UnitTests
{
    [Collection("Sequential")]
    public sealed class SystemNamespaceFilteringTests : LiteBusTestBase
    {
        [Fact]
        public void Register_MessageInNamespaceStartingWithSystem_ShouldRegisterAsMessage()
        {
            var registry = new MessageRegistry();

            registry.Register(typeof(SystematicEvent));

            registry.Should().HaveCount(1);
            registry.First().MessageType.Should().Be(typeof(SystematicEvent));
        }

        [Fact]
        public void Register_SystemNamespaceType_ShouldNotRegisterAsMessage()
        {
            var registry = new MessageRegistry();

            registry.Register(typeof(Uri));

            registry.Register(typeof(DateTimeOffset));

            registry.Register(typeof(List<string>));

            registry.Should().BeEmpty();
        }
    }
}

namespace Systematic.Domain.Events
{
    public sealed record SystematicEvent : IEvent;
}