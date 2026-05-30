using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

public sealed class PostgreSqlModuleRegistrationTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlModuleRegistrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void AddPostgreSqlInboxStorage_ShouldRegisterWriterLeaseAndStateRoles()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();

        var services = new ServiceCollection();
        services.AddLiteBus(configuration =>
        {
            configuration.AddPostgreSqlInboxStorage(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
            });
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IInboxStore>().Should().BeOfType<PostgreSqlInboxStore>();
        provider.GetRequiredService<IInboxLeaseStore>().Should().BeOfType<PostgreSqlInboxStore>();
        provider.GetRequiredService<IInboxStateStore>().Should().BeOfType<PostgreSqlInboxStore>();
        provider.GetRequiredService<PostgreSqlInboxStoreRegistration>().Options.TableName.Should().Be(options.TableName);
    }

    [Fact]
    public void AddPostgreSqlOutboxStorage_ShouldRegisterWriterLeaseAndStateRoles()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();

        var services = new ServiceCollection();
        services.AddLiteBus(configuration =>
        {
            configuration.AddPostgreSqlOutboxStorage(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
            });
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOutboxStore>().Should().BeOfType<PostgreSqlOutboxStore>();
        provider.GetRequiredService<IOutboxLeaseStore>().Should().BeOfType<PostgreSqlOutboxStore>();
        provider.GetRequiredService<IOutboxStateStore>().Should().BeOfType<PostgreSqlOutboxStore>();
        provider.GetRequiredService<PostgreSqlOutboxStoreRegistration>().Options.TableName.Should().Be(options.TableName);
    }
}
