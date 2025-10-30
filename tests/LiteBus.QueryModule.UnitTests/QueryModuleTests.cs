using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.QueryModule.UnitTests.UseCases;
using LiteBus.QueryModule.UnitTests.UseCases.GetProduct;
using LiteBus.QueryModule.UnitTests.UseCases.GetProductByCriteria;
using LiteBus.QueryModule.UnitTests.UseCases.ProblematicQuery;
using LiteBus.QueryModule.UnitTests.UseCases.QueryWithTag;
using LiteBus.QueryModule.UnitTests.UseCases.StreamProducts;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.QueryModule.UnitTests;

public sealed class QueryModuleTests : LiteBusTestBase
{
    [Fact]
    public async Task Mediating_GetProductQuery_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection().AddLiteBus(configuration =>
        {
            configuration.AddQueryModule(builder =>
            {
                builder.RegisterFromAssembly(typeof(GetProductQuery).Assembly);
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new GetProductQuery();

        // Act
        var queryResult = await queryMediator.QueryAsync(query);

        // Assert
        queryResult.Should().NotBeNull();
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
        var serviceProvider = new ServiceCollection().AddLiteBus(configuration =>
        {
            configuration.AddQueryModule(builder =>
            {
                builder.RegisterFromAssembly(typeof(GetProductByCriteriaQuery<>).Assembly);
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var queryPayload = new PriceCriteria { Min = 1, Max = 100 };
        var query = new GetProductByCriteriaQuery<PriceCriteria> { Payload = queryPayload };

        // Act
        var queryResult = await queryMediator.QueryAsync(query);

        // Assert
        queryResult.Should().NotBeNull();
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
        var serviceProvider = new ServiceCollection().AddLiteBus(configuration =>
        {
            configuration.AddQueryModule(builder =>
            {
                builder.RegisterFromAssembly(typeof(GetProductQuery).Assembly);
                builder.Register<StreamProductsQueryHandlerPostHandler2>();
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new StreamProductsQuery();

        // Act
        var queryResult = await queryMediator.StreamAsync(query).ToListAsync();

        // Assert
        queryResult.First().CorrelationId.Should().Be(query.CorrelationId);
        query.ExecutedTypes.Should().HaveCount(6);
        query.ExecutedTypes[0].Should().Be<GlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<StreamProductsQueryHandlerPreHandler>();
        query.ExecutedTypes[2].Should().Be<StreamProductsQueryHandler>();
        query.ExecutedTypes[3].Should().Be<StreamProductsQueryHandlerPostHandler1>();
        query.ExecutedTypes[4].Should().Be<StreamProductsQueryHandlerPostHandler2>();
        query.ExecutedTypes[5].Should().Be<GlobalQueryPostHandler>();
    }

    [Fact]
    public async Task mediating_a_query_with_exception_in_pre_handler_goes_through_error_handlers()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(configuration =>
        {
            configuration.AddQueryModule(builder =>
            {
                builder.RegisterFromAssembly(typeof(ProblematicQueryPreHandler).Assembly);
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new ProblematicQuery { ThrowExceptionInType = typeof(ProblematicQueryPreHandler) };

        // Act
        await queryMediator.QueryAsync(query);

        // Assert
        query.ExecutedTypes.Should().HaveCount(5);
        query.ExecutedTypes[0].Should().Be<GlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<ProblematicQueryPreHandler>();
        query.ExecutedTypes[2].Should().Be<GlobalQueryErrorHandler>();
        query.ExecutedTypes[3].Should().Be<ProblematicQueryErrorHandler>();
        query.ExecutedTypes[4].Should().Be<ProblematicQueryErrorHandler2>();
    }

    [Fact]
    public async Task mediating_a_query_with_exception_in_post_global_handler_goes_through_error_handlers()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(configuration =>
        {
            configuration.AddQueryModule(builder =>
            {
                builder.RegisterFromAssembly(typeof(ProblematicQueryPreHandler).Assembly);
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new ProblematicQuery { ThrowExceptionInType = typeof(GlobalQueryPostHandler) };

        // Act
        await queryMediator.QueryAsync(query);

        // Assert
        query.ExecutedTypes.Should().HaveCount(8);
        query.ExecutedTypes[0].Should().Be<GlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<ProblematicQueryPreHandler>();
        query.ExecutedTypes[2].Should().Be<ProblematicQueryHandler>();
        query.ExecutedTypes[3].Should().Be<ProblematicQueryPostHandler>();
        query.ExecutedTypes[4].Should().Be<GlobalQueryPostHandler>();
        query.ExecutedTypes[5].Should().Be<GlobalQueryErrorHandler>();
        query.ExecutedTypes[6].Should().Be<ProblematicQueryErrorHandler>();
        query.ExecutedTypes[7].Should().Be<ProblematicQueryErrorHandler2>();
    }

    [Fact]
    public async Task mediating_an_query_with_specified_tag_goes_through_handlers_with_that_tag_and_handlers_without_any_tag_correctly()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(configuration =>
        {
            configuration.AddQueryModule(builder =>
            {
                builder.RegisterFromAssembly(typeof(ProblematicQueryPreHandler).Assembly);
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new QueryWithTag();
        var settings = new QueryMediationSettings { Filters = { Tags = [Tags.Tag1] } };

        // Act
        await queryMediator.QueryAsync(query, settings);

        // Assert
        query.ExecutedTypes.Should().HaveCount(7);
        query.ExecutedTypes[0].Should().Be<GlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<QueryWithTagPreHandler1>();
        query.ExecutedTypes[2].Should().Be<QueryWithTagPreHandler3>();
        query.ExecutedTypes[3].Should().Be<QueryWithTagPreHandler4>();
        query.ExecutedTypes[4].Should().Be<QueryWithTagHandler1>();
        query.ExecutedTypes[5].Should().Be<QueryWithTagPostHandler1>();
        query.ExecutedTypes[6].Should().Be<GlobalQueryPostHandler>();
    }

    [Fact]
    public async Task mediating_the_an_query_with_both_all_available_tags_will_fail_as_there_are_two_main_handlers()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(configuration =>
        {
            configuration.AddQueryModule(builder =>
            {
                builder.RegisterFromAssembly(typeof(ProblematicQueryPreHandler).Assembly);
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new QueryWithTag();
        var settings = new QueryMediationSettings { Filters = { Tags = [Tags.Tag1, Tags.Tag2] } };

        // Act
        Func<Task> act = async () => await queryMediator.QueryAsync(query, settings);

        // Assert
        await act.Should().ThrowAsync<MultipleHandlerFoundException>();
    }

    [Fact]
    public async Task mediating_a_stream_query_that_is_aborted_in_pre_handler_goes_through_correct_handlers()
    {
        // Arrange
        var serviceProvider = new ServiceCollection().AddLiteBus(configuration =>
        {
            configuration.AddQueryModule(builder =>
            {
                builder.RegisterFromAssembly(typeof(GetProductQuery).Assembly);
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new StreamProductsQuery { AbortInPreHandler = true };

        // Act
        var queryResult = await queryMediator.StreamAsync(query).ToListAsync();

        // Assert
        queryResult.Should().BeEmpty();
        query.ExecutedTypes.Should().HaveCount(2);
        query.ExecutedTypes[0].Should().Be<GlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<StreamProductsQueryHandlerPreHandler>();
    }

    [Fact]
    public async Task Mediating_StreamQuery_PassesMetadataViaExecutionContext()
    {
        // Arrange
        var serviceProvider = new ServiceCollection().AddLiteBus(configuration =>
        {
            configuration.AddQueryModule(builder =>
            {
                builder.Register<StreamProductsQueryHandler>();
                builder.Register<StreamProductsQueryHandlerPostHandler1>();
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new StreamProductsQuery();

        // Act
        await queryMediator.StreamAsync(query).ToListAsync();

        // Assert
        // The post-handler should have retrieved the count from the execution context
        // and set it on the query object.
        query.RetrievedStreamCount.Should().Be(1);
    }
}