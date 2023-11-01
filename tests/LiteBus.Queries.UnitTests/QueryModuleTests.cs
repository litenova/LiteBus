using FluentAssertions;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using LiteBus.Queries.Abstractions;
using LiteBus.Queries.Extensions.MicrosoftDependencyInjection;
using LiteBus.Queries.UnitTests.UseCases;
using LiteBus.Queries.UnitTests.UseCases.GetProduct;
using LiteBus.Queries.UnitTests.UseCases.GetProductByCriteria;
using LiteBus.Queries.UnitTests.UseCases.StreamProducts;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Queries.UnitTests;

public sealed class QueryModuleTests
{
    [Fact]
    public async Task Mediating_GetProductQuery_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddQueryModule(builder => { builder.RegisterFromAssembly(typeof(GetProductQuery).Assembly); }); })
            .BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new GetProductQuery();

        // Act
        var queryResult = await queryMediator.QueryAsync(query);

        // Assert
        queryResult.CorrelationId.Should().Be(query.CorrelationId);
        query.ExecutedTypes.Should().HaveCount(6);

        query.ExecutedTypes[0].Should().Be<GlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<GetProductQueryHandlerPreHandler>();
        query.ExecutedTypes[2].Should().Be<GetProductQueryHandler>();
        query.ExecutedTypes[3].Should().Be<GetProductQueryHandlerPostHandler1>();
        query.ExecutedTypes[4].Should().Be<GetProductQueryHandlerPostHandler2>();
        query.ExecutedTypes[5].Should().Be<GlobalQueryPostHandler>();
    }

    [Fact]
    public async Task Mediating_GetProductByCriteriaQuery_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddQueryModule(builder => { builder.RegisterFromAssembly(typeof(GetProductByCriteriaQuery<>).Assembly); }); })
            .BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var queryPayload = new PriceCriteria { Min = 1, Max = 100 };

        var query = new GetProductByCriteriaQuery<PriceCriteria>
        {
            Payload = queryPayload
        };

        // Act
        var queryResult = await queryMediator.QueryAsync(query);

        // Assert
        queryResult.CorrelationId.Should().Be(query.CorrelationId);
        query.ExecutedTypes.Should().HaveCount(5);

        query.ExecutedTypes[0].Should().Be<GlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<GetProductByCriteriaQueryPreHandler<PriceCriteria>>();
        query.ExecutedTypes[2].Should().Be<GetProductByCriteriaQueryHandler<PriceCriteria>>();
        query.ExecutedTypes[3].Should().Be<GetProductByCriteriaQueryPostHandler<PriceCriteria>>();
        query.ExecutedTypes[4].Should().Be<GlobalQueryPostHandler>();
    }

    [Fact]
    public async Task Mediating_StreamProductsQuery_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddQueryModule(builder => { builder.RegisterFromAssembly(typeof(GetProductQuery).Assembly); }); })
            .BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new StreamProductsQuery();

        // Act
        var queryResult = await queryMediator.StreamAsync(query).ToListAsync();

        // Assert
        queryResult.First().CorrelationId.Should().Be(query.CorrelationId);
        query.ExecutedTypes.Should().HaveCount(5);

        query.ExecutedTypes[0].Should().Be<GlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<StreamProductsQueryHandlerPreHandler>();
        query.ExecutedTypes[2].Should().Be<StreamProductsQueryHandler>();
        query.ExecutedTypes[3].Should().Be<StreamProductsQueryHandlerPostHandler1>();
        query.ExecutedTypes[4].Should().Be<GlobalQueryPostHandler>();
    }
}