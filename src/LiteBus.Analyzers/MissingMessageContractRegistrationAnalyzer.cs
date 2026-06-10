using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
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
        var registeredContracts = ContractRegistrationAnalysis.CollectRegisteredContractTypes(
            context.Compilation,
            context.CancellationToken);
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

            if (ContractRegistrationAnalysis.HasMessageContractAttribute(handler.MessageType) ||
                registeredContracts.Contains(handler.MessageType, SymbolEqualityComparer.Default))
            {
                continue;
            }

            var closedTypeDisplay = ContractRegistrationAnalysis.GetClosedRegistrationTypeDisplay(handler.MessageType);

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.MissingMessageContractRegistration,
                handler.Location,
                HandlerAnalysis.GetMessageTypeDisplay(handler.MessageType),
                handler.HandlerType.Name,
                closedTypeDisplay));
        }
    }
}
