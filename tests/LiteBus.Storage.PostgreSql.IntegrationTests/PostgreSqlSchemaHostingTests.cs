using LiteBus.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Runtime.Abstractions;
using LiteBus.Storage.PostgreSql;
using LiteBus.Testing;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

public sealed class PostgreSqlSchemaHostingTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlSchemaHostingTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InboxSchemaBackgroundWork_WhenEnabled_ShouldCreateSchemaOnStartup()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions() with
        {
            EnsureSchemaCreationOnStartup = true
        };

        await using var provider = BuildInboxProvider(options);
        var backgroundWork = provider.GetRequiredService<PostgreSqlInboxSchemaBackgroundWork>();

        await backgroundWork.RunAsync(CancellationToken.None);

        var action = async () => await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InboxSchemaBackgroundWork_WhenDisabled_ShouldNotCreateSchemaOnStartup()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions() with
        {
            EnsureSchemaCreationOnStartup = false
        };

        await using var provider = BuildInboxProvider(options);
        var backgroundWork = provider.GetRequiredService<PostgreSqlInboxSchemaBackgroundWork>();

        await backgroundWork.RunAsync(CancellationToken.None);

        var action = async () => await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options);
        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>();
    }

    [Fact]
    public async Task InboxSchemaBackgroundWork_WhenValidationEnabled_ShouldValidateAfterEnsure()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions() with
        {
            EnsureSchemaCreationOnStartup = true,
            ValidateSchemaCreationOnStartup = true
        };

        await using var provider = BuildInboxProvider(options);
        var backgroundWork = provider.GetRequiredService<PostgreSqlInboxSchemaBackgroundWork>();

        var action = async () => await backgroundWork.RunAsync(CancellationToken.None);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OutboxSchemaBackgroundWork_WhenEnabled_ShouldCreateSchemaOnStartup()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions() with
        {
            EnsureSchemaCreationOnStartup = true
        };

        await using var provider = BuildOutboxProvider(options);
        var backgroundWork = provider.GetRequiredService<PostgreSqlOutboxSchemaBackgroundWork>();

        await backgroundWork.RunAsync(CancellationToken.None);

        var action = async () => await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OutboxSchemaBackgroundWork_WhenDisabled_ShouldNotCreateSchemaOnStartup()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions() with
        {
            EnsureSchemaCreationOnStartup = false
        };

        await using var provider = BuildOutboxProvider(options);
        var backgroundWork = provider.GetRequiredService<PostgreSqlOutboxSchemaBackgroundWork>();

        await backgroundWork.RunAsync(CancellationToken.None);

        var action = async () => await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options);
        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>();
    }

    [Fact]
    public async Task SchemaBackgroundWork_SecondRun_ShouldRemainIdempotent()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions() with
        {
            EnsureSchemaCreationOnStartup = true
        };

        await using var provider = BuildInboxProvider(options);
        var backgroundWork = provider.GetRequiredService<PostgreSqlInboxSchemaBackgroundWork>();

        await backgroundWork.RunAsync(CancellationToken.None);
        await backgroundWork.RunAsync(CancellationToken.None);

        await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options);
    }

    private ServiceProvider BuildInboxProvider(PostgreSqlInboxStoreOptions options)
    {
        var services = new ServiceCollection();
        services.AddLiteBus(configuration =>
        {
            configuration.AddPostgreSqlInboxStorage(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
            });
        });

        return services.BuildServiceProvider();
    }

    private ServiceProvider BuildOutboxProvider(PostgreSqlOutboxStoreOptions options)
    {
        var services = new ServiceCollection();
        services.AddLiteBus(configuration =>
        {
            configuration.AddPostgreSqlOutboxStorage(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
            });
        });

        return services.BuildServiceProvider();
    }
}
