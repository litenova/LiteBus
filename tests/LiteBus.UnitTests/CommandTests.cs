using System.Threading.Tasks;
using FluentAssertions;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Extensions.MicrosoftDependencyInjection;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using LiteBus.UnitTests.Data.Commands;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiteBus.UnitTests;

public class CommandTests
{
    [Fact]
    public async Task Send_FakeCommand_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
                              .AddLiteBus(configuration =>
                              {
                                  configuration.AddCommands(builder =>
                                  {
                                      builder.RegisterFrom(typeof(FakeCommand).Assembly);
                                  });
                              })
                              .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new FakeCommand();
        
        // Act
        await commandMediator.SendAsync(command);
        
        // Assert
        command.ExecutedTypes.Should().HaveCount(5);
    }
}