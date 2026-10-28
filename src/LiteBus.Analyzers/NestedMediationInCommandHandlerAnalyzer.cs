using System;
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
///         after the handler succeeds.
///     </para>
///     <para>
///         Reads through <c>IQueryMediator</c> are not reported: queries are side-effect free by contract (see
///         LB1003), so nesting a read nests no unit of work. <c>EvaluateAsync</c> guard checks are not reported
///         either, because they decide without acting. An <c>IMessageMediator.Mediate</c> call whose message is a
///         query is treated as a read for the same reason.
///     </para>
///     <para>
///         Disabled by default. Enable with <c>dotnet_diagnostic.LB1023.severity = warning</c> once the codebase is
///         ready to keep nested units of work out of command handlers.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NestedMediationInCommandHandlerAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    ///     Handle method names inspected for nested mediation.
    /// </summary>
    private static readonly string[] HandleMethodNames = ["HandleAsync", "Handle"];

    /// <summary>
    ///     Mediator method names that nest a unit of work, checked syntactically before binding.
    /// </summary>
    private static readonly string[] NestedMediationMethodNames = ["SendAsync", "TrySendAsync", "PublishAsync", "Mediate"];

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
    ///     The invoked name is checked first, so invocations that cannot match cost one string comparison. The enclosing
    ///     handle method is found by walking out of lambdas and local functions, so a send inside a lambda in the handle
    ///     method is reported as well.
    /// </remarks>
    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        if (context.Operation is not IInvocationOperation invocation ||
            invocation.Syntax is not InvocationExpressionSyntax syntax ||
            !IsNestedMediationName(syntax))
        {
            return;
        }

        if (GetEnclosingHandleMethod(context.ContainingSymbol) is not { } handleMethod)
        {
            return;
        }

        var handlerType = handleMethod.ContainingType;

        if (!SymbolEqualityComparer.Default.Equals(handlerType.ContainingAssembly, context.Compilation.Assembly) ||
            !CommandHandlerAnalysis.IsCommandHandler(handlerType, context.Compilation))
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
