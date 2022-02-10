using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using LiteBus.Queries.Abstractions;
using LiteBus.Queries.Extensions.MicrosoftDependencyInjection;
using LiteBus.UnitTests.Data.FakeGenericStreamQuery.Handlers;
using LiteBus.UnitTests.Data.FakeGenericStreamQuery.Messages;
using LiteBus.UnitTests.Data.FakeGenericStreamQuery.PostHandlers;
using LiteBus.UnitTests.Data.FakeGenericStreamQuery.PreHandlers;
using LiteBus.UnitTests.Data.FakeStreamQuery.Handlers;
using LiteBus.UnitTests.Data.FakeStreamQuery.Messages;
using LiteBus.UnitTests.Data.FakeStreamQuery.PostHandlers;
using LiteBus.UnitTests.Data.FakeStreamQuery.PreHandlers;
using LiteBus.UnitTests.Data.Shared.QueryGlobalPostHandlers;
using LiteBus.UnitTests.Data.Shared.QueryGlobalPreHandlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiteBus.UnitTests;

public class StreamQueryTests
{
    [Fact]
    public async Task StreamAsync_FakeStreamQuery_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
                              .AddLiteBus(configuration =>
                              {
                                  configuration.AddQueries(builder =>
                                  {
                                      // Global Handlers
                                      builder.RegisterPreHandler<FakeGlobalQueryPreHandler>();
                                      builder.RegisterPostHandler<FakeGlobalQueryPostHandler>();

                                      // Fake Query Handlers
                                      builder.RegisterPreHandler<FakeStreamQueryPreHandler>();
                                      builder.RegisterHandler<FakeStreamQueryHandler>();
                                      builder.RegisterPostHandler<FakeStreamQueryPostHandler>();
                                  });
                              })
                              .BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new FakeStreamQuery();

        // Act
        await queryMediator.StreamAsync(query).ToListAsync();

        // Assert
        query.ExecutedTypes.Should().HaveCount(5);
        query.ExecutedTypes[0].Should().Be<FakeGlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<FakeStreamQueryPreHandler>();
        query.ExecutedTypes[2].Should().Be<FakeStreamQueryHandler>();
        query.ExecutedTypes[3].Should().Be<FakeStreamQueryPostHandler>();
        query.ExecutedTypes[4].Should().Be<FakeGlobalQueryPostHandler>();
    }

    [Fact]
    public async Task StreamAsync_FakeStreamGenericQuery_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
                              .AddLiteBus(configuration =>
                              {
                                  configuration.AddQueries(builder =>
                                  {
                                      // Global Handlers
                                      builder.RegisterPreHandler<FakeGlobalQueryPreHandler>();
                                      builder.RegisterPostHandler<FakeGlobalQueryPostHandler>();

                                      // Fake Query Handlers
                                      builder.RegisterPreHandler(typeof(FakeGenericStreamQueryPreHandler<>));
                                      builder.RegisterHandler(typeof(FakeGenericStreamQueryHandler<>));
                                      builder.RegisterPostHandler(typeof(FakeGenericStreamQueryPostHandler<>));
                                  });
                              })
                              .BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new FakeGenericStreamQuery<string>();

        // Act
        await queryMediator.StreamAsync(query).ToListAsync();

        // Assert
        query.ExecutedTypes.Should().HaveCount(5);
        query.ExecutedTypes[0].Should().Be<FakeGlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<FakeGenericStreamQueryPreHandler<string>>();
        query.ExecutedTypes[2].Should().Be<FakeGenericStreamQueryHandler<string>>();
        query.ExecutedTypes[3].Should().Be<FakeGenericStreamQueryPostHandler<string>>();
        query.ExecutedTypes[4].Should().Be<FakeGlobalQueryPostHandler>();
    }
}