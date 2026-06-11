using System.Collections.Immutable;
using System.Linq;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

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
        context.RegisterCompilationStartAction(startContext =>
        {
            var registeredTypes = ContractRegistrationAnalysis.CollectRegisteredContractTypes(
                startContext.Compilation,
                startContext.CancellationToken);

            var registeredAssemblies = ContractRegistrationAnalysis.CollectRegisterFromAssemblyTargets(
                startContext.Compilation,
                startContext.CancellationToken);

            startContext.RegisterSyntaxNodeAction(
                nodeContext =>
                {
                    if (nodeContext.Node is not TypeDeclarationSyntax typeDeclaration)
                    {
                        return;
                    }

                    if (nodeContext.SemanticModel.GetDeclaredSymbol(typeDeclaration, nodeContext.CancellationToken) is not INamedTypeSymbol typeSymbol)
                    {
                        return;
                    }

                    if (typeSymbol.TypeKind == TypeKind.TypeParameter ||
                        HandlerAnalysis.IsGenericTypeDefinition(typeSymbol) ||
                        !ContractRegistrationAnalysis.HasMessageContractAttribute(typeSymbol) ||
                        !ContractRegistrationAnalysis.IsDurableMessageType(typeSymbol, startContext.Compilation))
                    {
                        return;
                    }

                    if (ContractRegistrationAnalysis.IsExplicitlyRegistered(
                            typeSymbol,
                            registeredTypes,
                            registeredAssemblies))
                    {
                        return;
                    }

                    var closedTypeDisplay = ContractRegistrationAnalysis.GetClosedRegistrationTypeDisplay(typeSymbol);

                    nodeContext.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.ExplicitMessageContractRegistration,
                        typeSymbol.Locations.FirstOrDefault() ?? Location.None,
                        closedTypeDisplay));
                },
                SyntaxKind.ClassDeclaration,
                SyntaxKind.StructDeclaration,
                SyntaxKind.RecordDeclaration);
        });
    }
}
