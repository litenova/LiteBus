using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports command handlers that send or publish another message through a mediator inside their handle method.
/// </summary>
/// <remarks>
///     <para>
///         Mediating a second message from inside a command handler nests a second unit of work inside the first:
///         the outer commit cannot roll the inner work back, and a retry of the outer handler runs the inner work
///         twice. Follow-up work belongs in the domain event collector or the transactional outbox, which drain
///         after the handler succeeds. Command pre-handlers and post-handlers are covered as well: their
///         <c>PreHandleAsync</c> and <c>PostHandleAsync</c> methods run inside the same unit of work.
///     </para>
///     <para>
///         Reads through <c>IQueryMediator</c> are not reported: queries are side-effect free by contract (see
///         LB1003), so nesting a read nests no unit of work. <c>EvaluateAsync</c> guard checks are not reported
///         either, because they decide without acting. An <c>IMessageMediator.Mediate</c> call whose message is a
///         query is treated as a read for the same reason.
///     </para>
///     <para>
///         Set <c>litebus_forbid_nested_commands = true</c> in <c>.editorconfig</c> to report this rule at
///         warning. Without the option the rule reports nothing. The runtime counterpart is
///         <c>CommandModuleBuilder.ForbidNestedCommands()</c>, which throws <c>NestedMediationException</c>
///         before the inner pipeline starts. An analyzer cannot see a composition call made in another
///         compilation, so the option pairs the build check with the runtime guarantee; the runtime stays the
///         guarantee.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NestedMediationInCommandHandlerAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    ///     The <c>.editorconfig</c> key that reports nested mediation at warning.
    /// </summary>
    internal const string ForbidNestedCommandsOption = "litebus_forbid_nested_commands";

    /// <summary>
    ///     Handle method names inspected for nested mediation.
    /// </summary>
    private static readonly string[] HandleMethodNames = ["HandleAsync", "Handle", "PreHandleAsync", "PostHandleAsync"];

    /// <summary>
    ///     Command pre-handler and post-handler interfaces covered alongside main command handlers.
    /// </summary>
    private static readonly string[] CommandStageHandlerInterfaceMetadataNames =
    [
        "LiteBus.Commands.Abstractions.ICommandPreHandler",
        "LiteBus.Commands.Abstractions.ICommandPreHandler`1",
        "LiteBus.Commands.Abstractions.ICommandPostHandler",
        "LiteBus.Commands.Abstractions.ICommandPostHandler`1",
        "LiteBus.Commands.Abstractions.ICommandPostHandler`2"
    ];

    /// <summary>
    ///     Messaging-level pre-handler and post-handler interfaces, reported only for command message types.
    /// </summary>
    private static readonly string[] MessagingStageHandlerInterfaceMetadataNames =
    [
        "LiteBus.Messaging.Abstractions.IMessagePreHandler`1",
        "LiteBus.Messaging.Abstractions.IMessagePostHandler`2"
    ];

    /// <summary>
    ///     Mediator method names that nest a unit of work, checked syntactically before binding.
    /// </summary>
    private static readonly string[] NestedMediationMethodNames = ["SendAsync", "TrySendAsync", "SendManyAsync", "TrySendManyAsync", "PublishAsync", "Mediate"];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.NestedMediationInCommandHandler];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    /// <summary>
    ///     Reports one invocation that sends or publishes through a mediator inside a command handler's handle method.
    /// </summary>
    /// <param name="context">The operation analysis context.</param>
    /// <remarks>
    ///     The invoked name is checked first, so invocations that cannot match cost one string comparison. Reporting
    ///     is gated on <c>litebus_forbid_nested_commands</c>, so a compilation without the option pays only the name
    ///     check. The enclosing handle method is found by walking out of lambdas and local functions, so a send inside
    ///     a lambda in the handle method is reported as well.
    /// </remarks>
    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        if (context.Operation is not IInvocationOperation invocation ||
            invocation.Syntax is not InvocationExpressionSyntax syntax ||
            !IsNestedMediationName(syntax))
        {
            return;
        }

        if (!IsForbidNestedCommandsEnabled(context, syntax.SyntaxTree))
        {
            return;
        }

        if (GetEnclosingHandleMethod(context.ContainingSymbol) is not { } handleMethod)
        {
            return;
        }

        var handlerType = handleMethod.ContainingType;

        if (!SymbolEqualityComparer.Default.Equals(handlerType.ContainingAssembly, context.Compilation.Assembly) ||
            !IsCommandPipelineHandler(handlerType, context.Compilation))
        {
            return;
        }

        var target = GetNestedMediationTarget(invocation.TargetMethod, context.Compilation);

        if (target is null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.NestedMediationInCommandHandler,
            syntax.GetLocation(),
            handlerType.Name,
            target));
    }

    /// <summary>
    ///     Determines whether nested mediation reporting is enabled for the invocation's syntax tree.
    /// </summary>
    /// <param name="context">The operation analysis context.</param>
    /// <param name="tree">The syntax tree holding the invocation.</param>
    /// <returns><see langword="true" /> when <c>litebus_forbid_nested_commands</c> is <c>true</c> for the tree; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    ///     Both the per-tree <c>.editorconfig</c> entry and the global options are read, the same two sources
    ///     <c>MissingDeclarationAnalyzer</c> reads for <c>litebus_required_declarations</c>. The per-tree value wins
    ///     when present, so one file can opt out of a project-wide setting.
    /// </remarks>
    private static bool IsForbidNestedCommandsEnabled(OperationAnalysisContext context, SyntaxTree tree)
    {
        if (context.Options.AnalyzerConfigOptionsProvider.GetOptions(tree).TryGetValue(ForbidNestedCommandsOption, out var treeValue) &&
            treeValue is not null)
        {
            return IsEnabledOptionValue(treeValue);
        }

        if (context.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(ForbidNestedCommandsOption, out var globalValue) &&
            globalValue is not null)
        {
            return IsEnabledOptionValue(globalValue);
        }

        return false;
    }

    /// <summary>
    ///     Determines whether a configured option value enables reporting.
    /// </summary>
    /// <param name="value">The raw configuration value.</param>
    /// <returns><see langword="true" /> when the value is <c>true</c>, ignoring case and surrounding whitespace; otherwise, <see langword="false" />.</returns>
    private static bool IsEnabledOptionValue(string value)
    {
        return string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Finds the handle method an operation belongs to, looking through lambdas and local functions.
    /// </summary>
    /// <param name="containingSymbol">The symbol that contains the analyzed operation.</param>
    /// <returns>The enclosing handle method, or <see langword="null" /> when the operation is not inside one.</returns>
    private static IMethodSymbol? GetEnclosingHandleMethod(ISymbol containingSymbol)
    {
        var symbol = containingSymbol;

        while (symbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction })
        {
            symbol = symbol.ContainingSymbol;
        }

        if (symbol is not IMethodSymbol method || method.ContainingType is null)
        {
            return null;
        }

        foreach (var name in HandleMethodNames)
        {
            if (string.Equals(method.Name, name, StringComparison.Ordinal))
            {
                return method;
            }
        }

        return null;
    }

    /// <summary>
    ///     Determines whether the type runs inside the command pipeline.
    /// </summary>
    /// <param name="type">The candidate handler type symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when the type handles commands or runs in their pre or post stage; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    ///     Main command handlers, command pre-handlers and post-handlers, and messaging-level pre-handlers and
    ///     post-handlers whose message type is a command all run inside the command unit of work, so a send from any
    ///     of them nests a second unit of work.
    /// </remarks>
    private static bool IsCommandPipelineHandler(INamedTypeSymbol type, Compilation compilation)
    {
        if (CommandHandlerAnalysis.IsCommandHandler(type, compilation))
        {
            return true;
        }

        foreach (var metadataName in CommandStageHandlerInterfaceMetadataNames)
        {
            if (LiteBusSymbols.ImplementsGenericInterface(type, compilation, metadataName))
            {
                return true;
            }
        }

        return ImplementsMessagingStageHandlerForCommands(type, compilation);
    }

    /// <summary>
    ///     Determines whether the type implements a messaging-level pre-handler or post-handler for commands.
    /// </summary>
    /// <param name="type">The candidate handler type symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when a pre-handler or post-handler message type is a command; otherwise, <see langword="false" />.</returns>
    private static bool ImplementsMessagingStageHandlerForCommands(INamedTypeSymbol type, Compilation compilation)
    {
        List<INamedTypeSymbol> handlers = [];

        foreach (var metadataName in MessagingStageHandlerInterfaceMetadataNames)
        {
            var handler = compilation.GetTypeByMetadataName(metadataName);

            if (handler is not null)
            {
                handlers.Add(handler);
            }
        }

        if (handlers.Count == 0)
        {
            return false;
        }

        foreach (var candidate in type.AllInterfaces)
        {
            foreach (var handler in handlers)
            {
                if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, handler) &&
                    candidate.TypeArguments.Length > 0 &&
                    CommandHandlerAnalysis.IsCommandMessage(candidate.TypeArguments[0], compilation))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether the invocation names a method that can nest a unit of work.
    /// </summary>
    /// <param name="invocation">The invocation syntax.</param>
    /// <returns><see langword="true" /> when the invoked name is a send or publish method; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    ///     The name is read syntactically so invocations that cannot match are skipped without binding.
    /// </remarks>
    private static bool IsNestedMediationName(InvocationExpressionSyntax invocation)
    {
        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.Text,
            _ => null
        };

        if (name is null)
        {
            return false;
        }

        foreach (var candidate in NestedMediationMethodNames)
        {
            if (string.Equals(name, candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Gets the nested mediation target display when the call nests a unit of work.
    /// </summary>
    /// <param name="method">The invoked method symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns>The mediator interface and method name, if the call nests a unit of work.</returns>
    private static string? GetNestedMediationTarget(IMethodSymbol method, Compilation compilation)
    {
        var receiverType = GetReceiverType(method);

        if (receiverType is null)
        {
            return null;
        }

        var commandMediator = compilation.GetTypeByMetadataName("LiteBus.Commands.Abstractions.ICommandMediator");

        if (commandMediator is not null &&
            IsMediatorReceiver(receiverType, commandMediator) &&
            method.Name is "SendAsync" or "TrySendAsync")
        {
            return "LiteBus.Commands.Abstractions.ICommandMediator." + method.Name;
        }

        var commandBatchMediator = compilation.GetTypeByMetadataName("LiteBus.Commands.Abstractions.ICommandBatchMediator");

        if (commandBatchMediator is not null &&
            IsMediatorReceiver(receiverType, commandBatchMediator) &&
            method.Name is "SendManyAsync" or "TrySendManyAsync")
        {
            return "LiteBus.Commands.Abstractions.ICommandBatchMediator." + method.Name;
        }

        var eventMediator = compilation.GetTypeByMetadataName("LiteBus.Events.Abstractions.IEventMediator");

        if (eventMediator is not null &&
            IsMediatorReceiver(receiverType, eventMediator) &&
            string.Equals(method.Name, "PublishAsync", StringComparison.Ordinal))
        {
            return "LiteBus.Events.Abstractions.IEventMediator." + method.Name;
        }

        var messageMediator = compilation.GetTypeByMetadataName("LiteBus.Messaging.Abstractions.IMessageMediator");

        if (messageMediator is not null &&
            IsMediatorReceiver(receiverType, messageMediator) &&
            string.Equals(method.Name, "Mediate", StringComparison.Ordinal) &&
            !MediatesOnlyAQuery(method, compilation))
        {
            return "LiteBus.Messaging.Abstractions.IMessageMediator." + method.Name;
        }

        return null;
    }

    /// <summary>
    ///     Gets the mediator type a call is made through, following extension method receivers.
    /// </summary>
    /// <param name="method">The invoked method symbol.</param>
    /// <returns>The receiver type, if it can be determined.</returns>
    /// <remarks>
    ///     The sugar overloads (<c>SendAsync(command, cancellationToken)</c> and the tagged forms) are extension
    ///     methods, so a positional call binds to the extension rather than to the interface method. The
    ///     <c>this</c> parameter still names the mediator the call goes through.
    /// </remarks>
    private static ITypeSymbol? GetReceiverType(IMethodSymbol method)
    {
        if (method.ReducedFrom is IMethodSymbol original)
        {
            return original.Parameters.Length > 0 ? original.Parameters[0].Type : null;
        }

        if (method.IsExtensionMethod)
        {
            return method.Parameters.Length > 0 ? method.Parameters[0].Type : null;
        }

        return method.ContainingType;
    }

    /// <summary>
    ///     Determines whether the receiver type is the mediator interface or implements it.
    /// </summary>
    /// <param name="receiverType">The receiver type symbol.</param>
    /// <param name="mediator">The mediator interface symbol.</param>
    /// <returns><see langword="true" /> when the call goes through the mediator; otherwise, <see langword="false" />.</returns>
    private static bool IsMediatorReceiver(ITypeSymbol receiverType, INamedTypeSymbol mediator)
    {
        if (SymbolEqualityComparer.Default.Equals(receiverType.OriginalDefinition, mediator))
        {
            return true;
        }

        foreach (var candidate in receiverType.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, mediator))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether an <c>IMessageMediator.Mediate</c> call only reads.
    /// </summary>
    /// <param name="method">The invoked <c>Mediate</c> method symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when the mediated message is known to be a query; otherwise, <see langword="false" />.</returns>
    private static bool MediatesOnlyAQuery(IMethodSymbol method, Compilation compilation)
    {
        return method.TypeArguments.Length > 0 &&
            CommandHandlerAnalysis.IsQueryMessage(method.TypeArguments[0], compilation);
    }
}
