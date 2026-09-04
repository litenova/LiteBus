using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Metadata;

/// <summary>
///     Verifies that the audit catalogue is derived from the declarations rather than maintained by hand.
/// </summary>
/// <remarks>
///     An audit catalogue and an authorization matrix are artifacts a permissioned application tends to build by hand
///     as a side effect of a migration, and then keep wrong. Both are pure functions of what the messages declare.
/// </remarks>
[Collection("Sequential")]
public sealed class AuditCatalogueTests : LiteBusTestBase
{
    [Fact]
    public void The_catalog_is_resolvable_at_runtime_and_not_only_during_composition()
    {
        var catalog = Build().GetRequiredService<IMessageCatalog>();

        catalog.Count.Should().BeGreaterThan(0);
        catalog.Audited().Should().NotBeEmpty();
    }

    [Fact]
    public void Rows_carry_what_each_audited_message_declares()
    {
        var rows = Build().GetRequiredService<IMessageCatalog>().ToRows();

        var transfer = rows.Should().ContainSingle(row => row.Action == "money.transfer-funds").Subject;
        transfer.MessageType.Should().Be<TransferFundsCommand>();
        transfer.Category.Should().Be("money");
        transfer.TargetKind.Should().Be("account");
        transfer.ReasonRequired.Should().BeFalse();
    }

    [Fact]
    public void Rows_are_ordered_by_action_so_two_runs_produce_the_same_document()
    {
        var rows = Build().GetRequiredService<IMessageCatalog>().ToRows();

        rows.Select(row => row.Action).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void An_exempt_or_undeclared_message_is_absent_from_the_catalogue()
    {
        var rows = Build().GetRequiredService<IMessageCatalog>().ToRows();

        // A catalogue of audited actions is what this produces. The exemptions and their rationales are a different
        // artifact answering a different question, read from the catalog itself.
        rows.Select(row => row.MessageType).Should().NotContain(typeof(PingCommand));
        rows.Select(row => row.MessageType).Should().NotContain(typeof(SweepStaleLocksCommand));
    }

    [Fact]
    public void The_markdown_renderer_produces_a_table_with_one_row_per_action()
    {
        var document = Build().GetRequiredService<IMessageCatalog>().ToMarkdown();

        document.Should().StartWith("| Action | Category | Target | Reason required | Message |");
        document.Should().Contain("| money.transfer-funds | money | account | no | TransferFundsCommand |");
        document.Should().EndWith("audited actions.");
    }

    [Fact]
    public void The_markdown_renderer_says_so_when_nothing_is_audited()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<SweepStaleLocksCommand>();
                    builder.Register<SweepStaleLocksCommandHandler>();
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<IMessageCatalog>().ToMarkdown()
            .Should().Be("No message declares an audited position.");
    }

    [Fact]
    public void An_application_projects_its_own_declarations_alongside_the_audit_rows()
    {
        var catalog = Build().GetRequiredService<IMessageCatalog>();

        // The other half of an authorization matrix. LiteBus builds the audit half; a permission is an application
        // value type, so the application projects it from the resolved metadata.
        var matrix = catalog
            .Where(entry => entry.Metadata.TryGet<RequiredAuthorization>(out _))
            .Select(entry =>
            {
                entry.Metadata.TryGet<RequiredAuthorization>(out var permission);
                return (entry.MessageType.Name, Permission: permission!.Name, entry.Audit?.Action);
            })
            .ToList();

        matrix.Should().Contain((nameof(TransferFundsCommand), "money.transfer", "money.transfer-funds"));
    }

    /// <summary>
    ///     Builds a provider over the described commands and one query.
    /// </summary>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider Build()
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<TransferFundsCommand>();
                    builder.Register<TransferFundsCommandHandler>();
                    builder.Register<TransferFundsCommandDefinition>();
                    builder.Register<PingCommand>();
                    builder.Register<PingCommandHandler>();
                    builder.Register<PingCommandDefinition>();
                    builder.Register<SweepStaleLocksCommand>();
                    builder.Register<SweepStaleLocksCommandHandler>();
                    builder.Register<BadlyNamedCommand>();
                    builder.Register<BadlyNamedCommandHandler>();
                });
            })
            .BuildServiceProvider();
    }
}
