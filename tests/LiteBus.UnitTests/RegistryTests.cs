using System;
using System.Linq;
using FluentAssertions;
using LiteBus.Registry;
using LiteBus.UnitTests.Data.PlainMessage;
using Xunit;

namespace LiteBus.UnitTests
{
    public class RegistryTests
    {
        [Fact]
        public void Registry_should_register_FakePlainMessage_handler_and_hooks_correctly()
        {
            // Arrange
            var messageRegistry = new MessageRegistry();

            // Act
            messageRegistry.Register(typeof(FakePlainMessage).Assembly);

            // Assert
            var descriptor = messageRegistry.Single(d => d.MessageType == typeof(FakePlainMessage));

            descriptor.Should().NotBeNull();
            
            descriptor.HandlerTypes.Should().ContainSingle(t => t == typeof(FakePlainMessageAsyncHandler));
            descriptor.HandlerTypes.Should().ContainSingle(t => t == typeof(FakePlainMessageAsyncHandlerWithResult));
            descriptor.HandlerTypes.Should().ContainSingle(t => t == typeof(FakePlainMessageStreamHandler));
            descriptor.HandlerTypes.Should().ContainSingle(t => t == typeof(FakePlainMessageSyncHandler));
            descriptor.HandlerTypes.Should().ContainSingle(t => t == typeof(FakePlainMessageSyncHandlerWithResult));

            descriptor.PostHandleHookTypes.Should().HaveCount(1);
            descriptor.PostHandleHookTypes.Should().ContainSingle(h => h == typeof(FakePlainMessagePostHandleHook));
            descriptor.PreHandleHookTypes.Should().HaveCount(1);
            descriptor.PreHandleHookTypes.Should().ContainSingle(h => h == typeof(FakePlainMessagePreHandleHook));
        }
    }
}