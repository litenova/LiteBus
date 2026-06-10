using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using LiteBus.Analyzers.Analysis;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports durable message types that declare <c>[MessageContract]</c> without explicit registration.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExplicitMessageContractRegistrationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.ExplicitMessageContractRegistration);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    /// <summary>
    ///     Reports attributed durable message types that rely on on-demand contract resolution only.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var registeredTypes = ContractRegistrationAnalysis.CollectRegisteredContractTypes(
            context.Compilation,
            context.CancellationToken);
        var registeredAssemblies = ContractRegistrationAnalysis.CollectRegisterFromAssemblyTargets(
            context.Compilation,
            context.CancellationToken);

        foreach (var tree in context.Compilation.SyntaxTrees)
        {
            var semanticModel = context.Compilation.GetSemanticModel(tree);

            foreach (var typeDeclaration in tree.GetRoot(context.CancellationToken).DescendantNodes()
                         .OfType<TypeDeclarationSyntax>())
            {
                if (semanticModel.GetDeclaredSymbol(typeDeclaration, context.CancellationToken) is not INamedTypeSymbol typeSymbol)
                {
                    continue;
                }

                if (typeSymbol.TypeKind == TypeKind.TypeParameter ||
                    HandlerAnalysis.IsGenericTypeDefinition(typeSymbol) ||
                    !ContractRegistrationAnalysis.HasMessageContractAttribute(typeSymbol) ||
                    !ContractRegistrationAnalysis.IsDurableMessageType(typeSymbol, context.Compilation))
                {
                    continue;
                }

                if (ContractRegistrationAnalysis.IsExplicitlyRegistered(
                        typeSymbol,
                        registeredTypes,
                        registeredAssemblies))
                {
                    continue;
                }

                var closedTypeDisplay = ContractRegistrationAnalysis.GetClosedRegistrationTypeDisplay(typeSymbol);

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ExplicitMessageContractRegistration,
                    typeSymbol.Locations.FirstOrDefault() ?? Location.None,
                    closedTypeDisplay));
            }
        }
    }
}
