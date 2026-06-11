using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Messaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

public sealed class PostgreSqlSchemaHostingTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlSchemaHostingTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InboxSchemaInitializer_WhenEnabled_ShouldCreateSchemaOnStartup()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions() with
        {
            EnsureSchemaCreationOnStartup = true
        };

        await using var provider = BuildInboxProvider(options);
        var backgroundService = provider.GetRequiredService<PostgreSqlInboxSchemaInitializer>();

        await backgroundService.RunAsync(CancellationToken.None);

        var action = async () => await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InboxSchemaInitializer_WhenValidateOnly_ShouldValidateWithoutEnsure()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions() with
        {
            EnsureSchemaCreationOnStartup = false,
            ValidateSchemaCreationOnStartup = true
        };

        await PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource, options);

        await using var provider = BuildInboxProvider(options);
        var backgroundService = provider.GetRequiredService<PostgreSqlInboxSchemaInitializer>();

        var action = async () => await backgroundService.RunAsync(CancellationToken.None);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InboxSchemaInitializer_WhenDisabled_ShouldNotCreateSchemaOnStartup()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions() with
        {
            EnsureSchemaCreationOnStartup = false,
            ValidateSchemaCreationOnStartup = false
        };

        await using var provider = BuildInboxProvider(options);
        var backgroundService = provider.GetRequiredService<PostgreSqlInboxSchemaInitializer>();

        await backgroundService.RunAsync(CancellationToken.None);

        var action = async () => await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options);
        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>();
    }

    [Fact]
    public async Task InboxSchemaInitializer_WhenValidationEnabled_ShouldValidateAfterEnsure()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions() with
        {
            EnsureSchemaCreationOnStartup = true,
            ValidateSchemaCreationOnStartup = true
        };

        await using var provider = BuildInboxProvider(options);
        var backgroundService = provider.GetRequiredService<PostgreSqlInboxSchemaInitializer>();

        var action = async () => await backgroundService.RunAsync(CancellationToken.None);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OutboxSchemaInitializer_WhenEnabled_ShouldCreateSchemaOnStartup()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions() with
        {
            EnsureSchemaCreationOnStartup = true
        };

        await using var provider = BuildOutboxProvider(options);
        var backgroundService = provider.GetRequiredService<PostgreSqlOutboxSchemaInitializer>();

        await backgroundService.RunAsync(CancellationToken.None);

        var action = async () => await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OutboxSchemaInitializer_WhenValidateOnly_ShouldValidateWithoutEnsure()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions() with
        {
            EnsureSchemaCreationOnStartup = false,
            ValidateSchemaCreationOnStartup = true
        };

        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, options);

        await using var provider = BuildOutboxProvider(options);
        var backgroundService = provider.GetRequiredService<PostgreSqlOutboxSchemaInitializer>();

        var action = async () => await backgroundService.RunAsync(CancellationToken.None);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OutboxSchemaInitializer_WhenDisabled_ShouldNotCreateSchemaOnStartup()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions() with
        {
            EnsureSchemaCreationOnStartup = false,
            ValidateSchemaCreationOnStartup = false
        };

        await using var provider = BuildOutboxProvider(options);
        var backgroundService = provider.GetRequiredService<PostgreSqlOutboxSchemaInitializer>();

        await backgroundService.RunAsync(CancellationToken.None);

        var action = async () => await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options);
        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>();
    }

    [Fact]
    public async Task SchemaInitializer_SecondRun_ShouldRemainIdempotent()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions() with
        {
            EnsureSchemaCreationOnStartup = true
        };

        await using var provider = BuildInboxProvider(options);
        var backgroundService = provider.GetRequiredService<PostgreSqlInboxSchemaInitializer>();

        await backgroundService.RunAsync(CancellationToken.None);
        await backgroundService.RunAsync(CancellationToken.None);

        await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options);
    }

    private ServiceProvider BuildInboxProvider(PostgreSqlInboxStoreOptions options)
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddInboxModule(inbox => inbox.UsePostgreSqlStorage(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
            }));
        });

        return services.BuildServiceProvider();
    }

    private ServiceProvider BuildOutboxProvider(PostgreSqlOutboxStoreOptions options)
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddOutboxModule(outbox => outbox.UsePostgreSqlStorage(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
            }));
        });

        return services.BuildServiceProvider();
    }
}