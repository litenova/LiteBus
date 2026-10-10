using System.Globalization;
using System.Text;

namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Generates realistic multi-file compilations that give every LiteBus analyzer facts to gather.
/// </summary>
internal static class LiteBusSourceGenerator
{
    /// <summary>
    ///     Generates the requested number of self-contained source files.
    /// </summary>
    /// <param name="fileCount">The number of files to generate.</param>
    /// <returns>The generated sources, one per file.</returns>
    internal static IReadOnlyList<string> Generate(int fileCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fileCount, 1);

        var sources = new List<string>(fileCount);

        for (var index = 0; index < fileCount; index++)
        {
            sources.Add(GenerateFile(index));
        }

        return sources;
    }

    /// <summary>
    ///     Generates one self-contained source file with LiteBus facts for every analyzer.
    /// </summary>
    /// <param name="index">The zero-based file index, used to keep type names unique.</param>
    /// <returns>The generated source.</returns>
    private static string GenerateFile(int index)
    {
        var id = index.ToString("D4", CultureInfo.InvariantCulture);
        var builder = new StringBuilder(4096);

        AppendHeader(builder);
        AppendMessages(builder, id);
        AppendHandlerExtensions(builder, id);
        AppendServices(builder, id);
        AppendRegistration(builder, id);

        if (index == 0)
        {
            AppendSharedBuilders(builder);
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Appends the using directives shared by every generated file.
    /// </summary>
    /// <param name="builder">The source under construction.</param>
    private static void AppendHeader(StringBuilder builder)
    {
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using System.Threading;");
        builder.AppendLine("using System.Threading.Tasks;");
        builder.AppendLine("using LiteBus.Commands.Abstractions;");
        builder.AppendLine("using LiteBus.Events.Abstractions;");
        builder.AppendLine("using LiteBus.Inbox.Abstractions;");
        builder.AppendLine("using LiteBus.Messaging.Abstractions;");
        builder.AppendLine("using LiteBus.Outbox.Abstractions;");
        builder.AppendLine("using LiteBus.Queries.Abstractions;");
        builder.AppendLine("using LiteBus.Inbox.Storage.EntityFrameworkCore;");
        builder.AppendLine("using LiteBus.Outbox.Storage.EntityFrameworkCore;");
        builder.AppendLine();
    }

    /// <summary>
    ///     Appends a command, a result command, a query, a stream query, and an event, each with its handler.
    /// </summary>
    /// <param name="builder">The source under construction.</param>
    /// <param name="id">The zero-padded file index kept unique across files.</param>
    private static void AppendMessages(StringBuilder builder, string id)
    {
        builder.AppendLine(CultureInfo.InvariantCulture, $"[MessageContract(\"cost.command.{id}\", 1)]");
        builder.AppendLine(CultureInfo.InvariantCulture, $"[Audited(\"cost.audit.{id}\")]");
        builder.AppendLine(CultureInfo.InvariantCulture, $"public sealed record CostCommand{id} : ICommand;");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"[HandlerTag(\"cost-tag-{id}\")]");
        builder.AppendLine(CultureInfo.InvariantCulture, $"public sealed class CostCommandHandler{id} : ICommandHandler<CostCommand{id}>");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{{");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    public Task HandleAsync(CostCommand{id} command, CancellationToken cancellationToken = default)");
        builder.AppendLine("        => Task.CompletedTask;");
        builder.AppendLine(CultureInfo.InvariantCulture, $"}}");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"public sealed record CostResultCommand{id}(string Name) : ICommand<int>;");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"public sealed class CostResultCommandHandler{id} : ICommandHandler<CostResultCommand{id}, int>");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{{");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    public Task<int> HandleAsync(CostResultCommand{id} command, CancellationToken cancellationToken = default)");
        builder.AppendLine("        => Task.FromResult(1);");
        builder.AppendLine(CultureInfo.InvariantCulture, $"}}");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"public sealed record CostQuery{id}(int ItemId) : IQuery<string>;");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"public sealed class CostQueryHandler{id} : IQueryHandler<CostQuery{id}, string>");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{{");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    public Task<string> HandleAsync(CostQuery{id} query, CancellationToken cancellationToken = default)");
        builder.AppendLine("        => Task.FromResult(\"ok\");");
        builder.AppendLine(CultureInfo.InvariantCulture, $"}}");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"public sealed record CostStreamQuery{id} : IStreamQuery<string>;");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"public sealed class CostStreamQueryHandler{id} : IStreamQueryHandler<CostStreamQuery{id}, string>");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{{");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    public async IAsyncEnumerable<string> StreamAsync(CostStreamQuery{id} query, CancellationToken cancellationToken = default)");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    {{");
        builder.AppendLine("        await Task.CompletedTask.ConfigureAwait(false);");
        builder.AppendLine("        yield return \"ok\";");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    }}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"}}");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"public sealed record CostEvent{id}(int EventId) : IEvent;");
        builder.AppendLine();
        builder.AppendLine("[HandlerPriority(10)]");
        builder.AppendLine(CultureInfo.InvariantCulture, $"public sealed class CostEventHandler{id} : IEventHandler<CostEvent{id}>");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{{");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    public Task HandleAsync(CostEvent{id} message, CancellationToken cancellationToken = default)");
        builder.AppendLine("        => Task.CompletedTask;");
        builder.AppendLine(CultureInfo.InvariantCulture, $"}}");
        builder.AppendLine();
    }

    /// <summary>
    ///     Appends a typed shortcut and a supported open generic pre-handler.
    /// </summary>
    /// <param name="builder">The source under construction.</param>
    /// <param name="id">The zero-padded file index kept unique across files.</param>
    private static void AppendHandlerExtensions(StringBuilder builder, string id)
    {
        builder.AppendLine(CultureInfo.InvariantCulture, $"public sealed class CostShortcut{id} : ICommandShortcut<CostCommand{id}>");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{{");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    public Task<Shortcut> TryAnswerAsync(CostCommand{id} message, CancellationToken cancellationToken = default)");
        builder.AppendLine("        => Task.FromResult(Shortcut.None);");
        builder.AppendLine(CultureInfo.InvariantCulture, $"}}");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"public sealed class CostLogger{id}<TCommand> : ICommandPreHandler<TCommand>");
        builder.AppendLine("    where TCommand : ICommand");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{{");
        builder.AppendLine("    public Task PreHandleAsync(TCommand command, CancellationToken cancellationToken = default)");
        builder.AppendLine("        => Task.CompletedTask;");
        builder.AppendLine(CultureInfo.InvariantCulture, $"}}");
        builder.AppendLine();
    }

    /// <summary>
    ///     Appends transactional services, an inbox scheduler, and a tagged mediation sender.
    /// </summary>
    /// <param name="builder">The source under construction.</param>
    /// <param name="id">The zero-padded file index kept unique across files.</param>
    private static void AppendServices(StringBuilder builder, string id)
    {
        builder.AppendLine(CultureInfo.InvariantCulture, $"public sealed class CostOutboxService{id}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{{");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    public CostOutboxService{id}(ITransactionalOutboxStore outbox)");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    {{");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    }}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"}}");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"public sealed class CostInboxService{id}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{{");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    public CostInboxService{id}(ITransactionalInboxStore inbox)");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    {{");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    }}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"}}");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"public sealed class CostInboxScheduler{id}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{{");
        builder.AppendLine("    private readonly IInbox _inbox;");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"    public CostInboxScheduler{id}(IInbox inbox)");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    {{");
        builder.AppendLine("        _inbox = inbox;");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    }}");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"    public Task ScheduleAsync(CostCommand{id} command, CancellationToken cancellationToken)");
        builder.AppendLine(CultureInfo.InvariantCulture, $"        => _inbox.AcceptAsync(command, cancellationToken: cancellationToken);");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"    public Task ScheduleResultAsync(CostResultCommand{id} command, CancellationToken cancellationToken)");
        builder.AppendLine(CultureInfo.InvariantCulture, $"        => _inbox.AcceptAsync(command, cancellationToken: cancellationToken);");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"    public Task ScheduleBatchAsync(CostCommand{id} command, CancellationToken cancellationToken)");
        builder.AppendLine(CultureInfo.InvariantCulture, $"        => _inbox.AcceptBatchAsync([InboxAcceptItem.From(command)], cancellationToken);");
        builder.AppendLine(CultureInfo.InvariantCulture, $"}}");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"public sealed class CostSender{id}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{{");
        builder.AppendLine("    public void Send(ICommandMediator mediator)");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    {{");
        builder.AppendLine("        var settings = new CommandMediationSettings");
        builder.AppendLine(CultureInfo.InvariantCulture, $"        {{");
        builder.AppendLine("            Routing = new CommandRoutingSettings");
        builder.AppendLine(CultureInfo.InvariantCulture, $"            {{");
        builder.AppendLine(CultureInfo.InvariantCulture, $"                Tags = new List<string> {{ \"cost-tag-{id}\" }}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"            }}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"        }};");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    }}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"}}");
        builder.AppendLine();
    }

    /// <summary>
    ///     Appends contract registration calls and module builder configuration.
    /// </summary>
    /// <param name="builder">The source under construction.</param>
    /// <param name="id">The zero-padded file index kept unique across files.</param>
    private static void AppendRegistration(StringBuilder builder, string id)
    {
        builder.AppendLine(CultureInfo.InvariantCulture, $"public sealed class CostContractsRegistry{id}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{{");
        builder.AppendLine("    public void Register<T>(string name, int version)");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    {{");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    }}");
        builder.AppendLine();
        builder.AppendLine("    public void RegisterFromAssembly(System.Reflection.Assembly assembly)");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    {{");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    }}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"}}");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"public static class CostContractConfiguration{id}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{{");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    public static void Configure(CostContractsRegistry{id} contracts)");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    {{");
        builder.AppendLine(CultureInfo.InvariantCulture, $"        contracts.Register<CostCommand{id}>(\"cost.command.{id}\", 1);");
        builder.AppendLine(CultureInfo.InvariantCulture, $"        contracts.RegisterFromAssembly(typeof(CostCommand{id}).Assembly);");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    }}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"}}");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"public static class CostModuleConfiguration{id}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{{");
        builder.AppendLine("    public static void ConfigureInbox(InboxModuleBuilder inbox)");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    {{");
        builder.AppendLine("        inbox.UseInProcessDispatch();");
        builder.AppendLine("        inbox.EnableInboxProcessor();");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    }}");
        builder.AppendLine();
        builder.AppendLine("    public static void ConfigureOutbox(OutboxModuleBuilder outbox)");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    {{");
        builder.AppendLine("        outbox.UseInProcessDispatch();");
        builder.AppendLine("        outbox.EnableOutboxProcessor();");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    }}");
        builder.AppendLine();
        builder.AppendLine("    public static void ConfigureInboxStorage(EfCoreInboxStorageModuleBuilder builder)");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    {{");
        builder.AppendLine("        builder.EnableSaveChangesInterceptor();");
        builder.AppendLine("        builder.EnforceTransactionalSetup();");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    }}");
        builder.AppendLine();
        builder.AppendLine("    public static void ConfigureOutboxStorage(EfCoreOutboxStorageModuleBuilder builder)");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    {{");
        builder.AppendLine("        builder.EnableSaveChangesInterceptor();");
        builder.AppendLine("        builder.EnforceTransactionalSetup();");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    }}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"}}");
        builder.AppendLine();
    }

    /// <summary>
    ///     Appends the fake module builders once, so invocations resolve without duplicating types.
    /// </summary>
    /// <param name="builder">The source under construction.</param>
    private static void AppendSharedBuilders(StringBuilder builder)
    {
        builder.AppendLine("namespace LiteBus.Inbox.Abstractions");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{{");
        builder.AppendLine("    public sealed class InboxModuleBuilder");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    {{");
        builder.AppendLine("        public InboxModuleBuilder EnableInboxProcessor() => this;");
        builder.AppendLine("        public InboxModuleBuilder UseInProcessDispatch() => this;");
        builder.AppendLine("        public InboxModuleBuilder RegisterDispatcher(object module) => this;");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    }}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"}}");
        builder.AppendLine();
        builder.AppendLine("namespace LiteBus.Outbox.Abstractions");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{{");
        builder.AppendLine("    public sealed class OutboxModuleBuilder");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    {{");
        builder.AppendLine("        public OutboxModuleBuilder EnableOutboxProcessor() => this;");
        builder.AppendLine("        public OutboxModuleBuilder UseInProcessDispatch() => this;");
        builder.AppendLine("        public OutboxModuleBuilder RegisterDispatcher(object module) => this;");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    }}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"}}");
        builder.AppendLine();
        builder.AppendLine("namespace LiteBus.Inbox.Storage.EntityFrameworkCore");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{{");
        builder.AppendLine("    public sealed class EfCoreInboxStorageModuleBuilder");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    {{");
        builder.AppendLine("        public EfCoreInboxStorageModuleBuilder EnforceTransactionalSetup() => this;");
        builder.AppendLine("        public EfCoreInboxStorageModuleBuilder EnableSaveChangesInterceptor() => this;");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    }}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"}}");
        builder.AppendLine();
        builder.AppendLine("namespace LiteBus.Outbox.Storage.EntityFrameworkCore");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{{");
        builder.AppendLine("    public sealed class EfCoreOutboxStorageModuleBuilder");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    {{");
        builder.AppendLine("        public EfCoreOutboxStorageModuleBuilder EnforceTransactionalSetup() => this;");
        builder.AppendLine("        public EfCoreOutboxStorageModuleBuilder EnableSaveChangesInterceptor() => this;");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    }}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"}}");
    }
}
