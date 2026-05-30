using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.QueryModule.UnitTests.UseCases.GetProduct;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.QueryModule.UnitTests;

public sealed class QueryMediatorValidationTests : LiteBusTestBase
{
    [Fact]
    public async Task QueryAsync_WithNullQuery_ThrowsArgumentNullException()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddQueryModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(GetProductQuery).Assembly);
                });
            })
            .BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();

        var act = async () => await queryMediator.QueryAsync<GetProductQueryResult>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task StreamAsync_WithNullQuery_ThrowsArgumentNullException()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddQueryModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(GetProductQuery).Assembly);
                });
            })
            .BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();

        var act = async () => await queryMediator.StreamAsync<GetProductQueryResult>(null!).ToListAsync();

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
