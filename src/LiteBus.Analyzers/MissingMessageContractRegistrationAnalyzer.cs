using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using LiteBus.Analyzers.Analysis;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports handled message types that lack durable contract registration.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingMessageContractRegistrationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.MissingMessageContractRegistration);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    /// <summary>
    ///     Reports handled message types without durable contract registration.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var registeredContracts = CollectRegisteredContractTypes(context.Compilation, context.CancellationToken);
        var handlers = HandlerAnalysis.CollectHandlerRegistrations(context.Compilation, context.CancellationToken)
            .Where(handler => handler.Pipeline is "command" or "event")
            .ToList();

        foreach (var handler in handlers)
        {
            if (handler.MessageType.TypeKind == TypeKind.TypeParameter ||
                HandlerAnalysis.IsGenericTypeDefinition(handler.MessageType))
            {
                continue;
            }

            if (HasMessageContractAttribute(handler.MessageType) ||
                registeredContracts.Contains(handler.MessageType, SymbolEqualityComparer.Default))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.MissingMessageContractRegistration,
                handler.Location,
                HandlerAnalysis.GetMessageTypeDisplay(handler.MessageType),
                handler.HandlerType.Name));
        }
    }

    /// <summary>
    ///     Determines whether the message type declares <see cref="MessageContractAttribute" />.
    /// </summary>
    /// <param name="messageType">The message type symbol.</param>
    /// <returns><see langword="true" /> when the attribute is present; otherwise, <see langword="false" />.</returns>
    private static bool HasMessageContractAttribute(ITypeSymbol messageType)
    {
        if (messageType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        foreach (var attribute in namedType.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "MessageContractAttribute")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Collects message types registered through <c>Contracts.Register</c> calls.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The registered message type symbols.</returns>
    private static ImmutableHashSet<ITypeSymbol> CollectRegisteredContractTypes(
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var builder = ImmutableHashSet.CreateBuilder<ITypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            foreach (var invocation in syntaxTree.GetRoot(cancellationToken).DescendantNodes()
                         .OfType<InvocationExpressionSyntax>())
            {
                var model = compilation.GetSemanticModel(syntaxTree);
                var method = model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;

                if (method is null || method.Name != "Register")
                {
                    continue;
                }

                if (method.TypeArguments.Length > 0)
                {
                    builder.Add(method.TypeArguments[0]);
                    continue;
                }

                if (invocation.ArgumentList.Arguments.Count == 0)
                {
                    continue;
                }

                var typeArgument = model.GetTypeInfo(invocation.ArgumentList.Arguments[0].Expression, cancellationToken).Type;

                if (typeArgument is not null)
                {
                    builder.Add(typeArgument);
                }
            }
        }

        return builder.ToImmutable();
    }
}
