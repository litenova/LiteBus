using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports command and query types that state no audit position.
/// </summary>
/// <remarks>
///     <para>
///         Auditing standards ask for the selection of audited events to be documented along with its rationale. A
///         message that carries neither <c>[Audited]</c> nor <c>[AuditExempt]</c> and has no audit definition is
///         indistinguishable from one nobody considered, which is exactly what the requirement exists to prevent.
///     </para>
///     <para>
///         The rule is disabled by default, because enabling it silently would break every existing compilation. Turn it
///         on with <c>dotnet_diagnostic.LB1018.severity = warning</c> in <c>.editorconfig</c> once the codebase declares
///         its position.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingAuditDeclarationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    ///     The metadata name of the attribute declaring that a message is audited.
    /// </summary>
    private const string AuditedAttributeMetadataName = "LiteBus.Messaging.Abstractions.AuditedAttribute";

    /// <summary>
    ///     The metadata name of the attribute declaring that a message is deliberately not audited.
    /// </summary>
    private const string AuditExemptAttributeMetadataName = "LiteBus.Messaging.Abstractions.AuditExemptAttribute";

    /// <summary>
    ///     The metadata name of the open generic audit definition facet.
    /// </summary>
    private const string AuditDefinitionMetadataName = "LiteBus.Messaging.Abstractions.IAuditDefinition`1";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.MissingAuditDeclaration];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    /// <summary>
    ///     Reports command and query types that declare no audit position.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var auditedAttribute = context.Compilation.GetTypeByMetadataName(AuditedAttributeMetadataName);
        var exemptAttribute = context.Compilation.GetTypeByMetadataName(AuditExemptAttributeMetadataName);
        var auditDefinition = context.Compilation.GetTypeByMetadataName(AuditDefinitionMetadataName);

        if (auditedAttribute is null && exemptAttribute is null && auditDefinition is null)
        {
            // The audit contracts are not referenced, so the codebase has not opted into auditing at all.
            return;
        }

        var declaredByDefinition = CollectDefinedMessageTypes(context, auditDefinition);

        foreach (var kind in new[] { MessageKind.Command, MessageKind.Query, MessageKind.StreamQuery })
        {
            foreach (var message in MessageAnalysis.CollectMessageTypes(context.Compilation, kind, context.CancellationToken))
            {
                if (message.MessageType.IsAbstract ||
                    !SymbolEqualityComparer.Default.Equals(message.MessageType.ContainingAssembly, context.Compilation.Assembly))
                {
                    continue;
                }

                if (HasAuditAttribute(message.MessageType, auditedAttribute, exemptAttribute) ||
                    declaredByDefinition.Contains(message.MessageType))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MissingAuditDeclaration,
                    LiteBusSymbols.GetDiagnosticLocation(context.Compilation, message.Location),
                    message.MessageType.Name));
            }
        }
    }

    /// <summary>
    ///     Collects message types covered by an audit definition facet declared in the analyzed assembly.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="auditDefinition">The open generic audit definition symbol, when referenced.</param>
    /// <returns>The message types declared by a definition.</returns>
    private static ImmutableHashSet<INamedTypeSymbol> CollectDefinedMessageTypes(
        CompilationAnalysisContext context,
        INamedTypeSymbol? auditDefinition)
    {
        if (auditDefinition is null)
        {
            return ImmutableHashSet.Create<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        }

        var builder = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        CollectFromNamespace(context.Compilation.Assembly.GlobalNamespace, auditDefinition, builder, context.CancellationToken);
        return builder.ToImmutable();
    }

    /// <summary>
    ///     Walks a namespace for definition classes and records the message types they describe.
    /// </summary>
    /// <param name="namespaceSymbol">The namespace to walk.</param>
    /// <param name="auditDefinition">The open generic audit definition symbol.</param>
    /// <param name="builder">The set of described message types being built.</param>
    /// <param name="cancellationToken">The analysis cancellation token.</param>
    private static void CollectFromNamespace(
        INamespaceSymbol namespaceSymbol,
        INamedTypeSymbol auditDefinition,
        ImmutableHashSet<INamedTypeSymbol>.Builder builder,
        CancellationToken cancellationToken)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (member)
            {
                case INamespaceSymbol nested:
                    CollectFromNamespace(nested, auditDefinition, builder, cancellationToken);
                    break;
                case INamedTypeSymbol type:
                    CollectFromType(type, auditDefinition, builder, cancellationToken);
                    break;
            }
        }
    }

    /// <summary>
    ///     Records the message types described by one type and its nested types.
    /// </summary>
    /// <param name="type">The candidate definition type.</param>
    /// <param name="auditDefinition">The open generic audit definition symbol.</param>
    /// <param name="builder">The set of described message types being built.</param>
    /// <param name="cancellationToken">The analysis cancellation token.</param>
    private static void CollectFromType(
        INamedTypeSymbol type,
        INamedTypeSymbol auditDefinition,
        ImmutableHashSet<INamedTypeSymbol>.Builder builder,
        CancellationToken cancellationToken)
    {
        foreach (var contract in type.AllInterfaces)
        {
            if (contract.IsGenericType &&
                SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, auditDefinition) &&
                contract.TypeArguments.Length == 1 &&
                contract.TypeArguments[0] is INamedTypeSymbol described)
            {
                builder.Add(described);
            }
        }

        foreach (var nested in type.GetTypeMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            CollectFromType(nested, auditDefinition, builder, cancellationToken);
        }
    }

    /// <summary>
    ///     Determines whether a message type carries an audit attribute.
    /// </summary>
    /// <param name="messageType">The message type symbol.</param>
    /// <param name="auditedAttribute">The audited attribute symbol, when referenced.</param>
    /// <param name="exemptAttribute">The exempt attribute symbol, when referenced.</param>
    /// <returns><see langword="true" /> when the message states its audit position by attribute.</returns>
    private static bool HasAuditAttribute(
        INamedTypeSymbol messageType,
        INamedTypeSymbol? auditedAttribute,
        INamedTypeSymbol? exemptAttribute)
    {
        return messageType.GetAttributes().Any(attribute =>
            (auditedAttribute is not null && SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, auditedAttribute)) ||
            (exemptAttribute is not null && SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, exemptAttribute)));
    }
}
