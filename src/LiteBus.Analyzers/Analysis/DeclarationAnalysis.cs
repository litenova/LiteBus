using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Shared logic for deciding whether a message states a position on one metadata value type.
/// </summary>
/// <remarks>
///     Both LB1018 and LB1020 ask the same question about a different value type, so the answer lives here once.
///     LB1018 is the preconfigured case for <c>AuditDeclaration</c>; LB1020 reads its value types from configuration.
/// </remarks>
internal static class DeclarationAnalysis
{
    /// <summary>
    ///     The metadata name of the open generic definition contract.
    /// </summary>
    private const string MessageDefinitionMetadataName = "LiteBus.Messaging.Abstractions.IMessageDefinition`2";

    private const string DescribingDefinitionMetadataName = "LiteBus.Messaging.Abstractions.IMessageDefinition`1";

    private const string MessageDeclarationsMetadataName = "LiteBus.Messaging.Abstractions.IMessageDeclarations";

    private const string AuditDeclarationMetadataName = "LiteBus.Messaging.Abstractions.AuditDeclaration";

    private const string DescribeMethodName = "Describe";

    /// <summary>
    ///     The metadata name of the annotation stating which value an attribute declares.
    /// </summary>
    private const string MessageDeclarationAttributeMetadataName =
        "LiteBus.Messaging.Abstractions.MessageDeclarationAttribute";

    /// <summary>
    ///     The metadata name of the general exemption attribute.
    /// </summary>
    private const string DeclarationExemptAttributeMetadataName =
        "LiteBus.Messaging.Abstractions.DeclarationExemptAttribute";

    /// <summary>
    ///     The message kinds a declaration requirement applies to.
    /// </summary>
    private static readonly MessageKind[] DeclarableKinds =
        [MessageKind.Command, MessageKind.Query, MessageKind.StreamQuery];

    /// <summary>
    ///     Collects the message types in the analyzed assembly that state no position on the given value type.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="valueType">The metadata value type the messages must declare or be exempt from.</param>
    /// <returns>The offending message registrations, in discovery order.</returns>
    /// <remarks>
    ///     Only types declared in the analyzed assembly are reported. A message from a referenced assembly cannot be
    ///     annotated from here, so reporting it would produce a warning with no available fix.
    /// </remarks>
    public static ImmutableArray<MessageTypeRegistration> CollectUndeclared(
        CompilationAnalysisContext context,
        INamedTypeSymbol valueType)
    {
        var declaredByDefinition = CollectDefinedMessageTypes(context.Compilation, valueType, context.CancellationToken);
        var declaringAttributes = CollectDeclaringAttributes(context.Compilation, valueType, context.CancellationToken);
        var exemptAttribute = context.Compilation.GetTypeByMetadataName(DeclarationExemptAttributeMetadataName);

        var builder = ImmutableArray.CreateBuilder<MessageTypeRegistration>();

        foreach (var kind in DeclarableKinds)
        {
            foreach (var message in MessageAnalysis.CollectMessageTypes(context.Compilation, kind, context.CancellationToken))
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                if (message.MessageType.IsAbstract ||
                    !SymbolEqualityComparer.Default.Equals(message.MessageType.ContainingAssembly, context.Compilation.Assembly))
                {
                    continue;
                }

                if (CoveredByDefinition(message.MessageType, declaredByDefinition) ||
                    HasDeclaringAttribute(message.MessageType, declaringAttributes) ||
                    HasExemption(message.MessageType, valueType, exemptAttribute))
                {
                    continue;
                }

                builder.Add(message);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Determines whether a definition declares the value for the message or for something it derives from.
    /// </summary>
    /// <param name="messageType">The message type being checked.</param>
    /// <param name="declaredByDefinition">The types a definition declares the value for.</param>
    /// <returns><see langword="true" /> when a definition covers the message.</returns>
    /// <remarks>
    ///     A declaration applies to the type it names and to every message assignable to it, so a definition written
    ///     for a base command or a marker interface satisfies the requirement for the whole family.
    /// </remarks>
    private static bool CoveredByDefinition(
        INamedTypeSymbol messageType,
        ImmutableHashSet<INamedTypeSymbol> declaredByDefinition)
    {
        if (declaredByDefinition.IsEmpty)
        {
            return false;
        }

        if (declaredByDefinition.Contains(messageType))
        {
            return true;
        }

        for (var baseType = messageType.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (declaredByDefinition.Contains(baseType))
            {
                return true;
            }
        }

        return messageType.AllInterfaces.Any(declaredByDefinition.Contains);
    }

    /// <summary>
    ///     Determines whether the message carries an attribute annotated as declaring the value type.
    /// </summary>
    /// <param name="messageType">The message type being checked.</param>
    /// <param name="declaringAttributes">The attribute classes annotated as declaring the value type.</param>
    /// <returns><see langword="true" /> when the message carries one of them.</returns>
    private static bool HasDeclaringAttribute(
        INamedTypeSymbol messageType,
        ImmutableHashSet<INamedTypeSymbol> declaringAttributes)
    {
        return !declaringAttributes.IsEmpty
               && messageType.GetAttributes().Any(attribute =>
                   attribute.AttributeClass is not null && declaringAttributes.Contains(attribute.AttributeClass));
    }

    /// <summary>
    ///     Determines whether the message records an exemption from declaring the value type.
    /// </summary>
    /// <param name="messageType">The message type being checked.</param>
    /// <param name="valueType">The metadata value type in question.</param>
    /// <param name="exemptAttribute">The exemption attribute symbol, when referenced.</param>
    /// <returns><see langword="true" /> when an exemption covers the value type.</returns>
    private static bool HasExemption(
        INamedTypeSymbol messageType,
        INamedTypeSymbol valueType,
        INamedTypeSymbol? exemptAttribute)
    {
        if (exemptAttribute is null)
        {
            return false;
        }

        foreach (var attribute in messageType.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, exemptAttribute) ||
                attribute.ConstructorArguments.Length == 0)
            {
                continue;
            }

            if (attribute.ConstructorArguments[0].Value is INamedTypeSymbol exempted &&
                SymbolEqualityComparer.Default.Equals(exempted, valueType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Collects the message types a definition in the analyzed assembly declares the value type for.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="valueType">The metadata value type in question.</param>
    /// <param name="cancellationToken">The analysis cancellation token.</param>
    /// <returns>The message types covered by a definition.</returns>
    private static ImmutableHashSet<INamedTypeSymbol> CollectDefinedMessageTypes(
        Compilation compilation,
        INamedTypeSymbol valueType,
        CancellationToken cancellationToken)
    {
        var definitionContract = compilation.GetTypeByMetadataName(MessageDefinitionMetadataName);
        var describingContract = compilation.GetTypeByMetadataName(DescribingDefinitionMetadataName);

        if (definitionContract is null && describingContract is null)
        {
            return ImmutableHashSet.Create<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        }

        var declarations = compilation.GetTypeByMetadataName(MessageDeclarationsMetadataName);
        var auditDeclaration = compilation.GetTypeByMetadataName(AuditDeclarationMetadataName);
        var builder = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var type in EnumerateTypes(compilation.Assembly.GlobalNamespace, cancellationToken))
        {
            foreach (var contract in type.AllInterfaces)
            {
                if (!contract.IsGenericType)
                {
                    continue;
                }

                // The keyed shape names the value type in the contract, so the type arguments settle it.
                if (definitionContract is not null &&
                    SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, definitionContract) &&
                    contract.TypeArguments.Length == 2)
                {
                    if (SymbolEqualityComparer.Default.Equals(contract.TypeArguments[1], valueType) &&
                        contract.TypeArguments[0] is INamedTypeSymbol described)
                    {
                        builder.Add(described);
                    }

                    continue;
                }

                // The Describe shape names nothing in the contract, so what it declares has to be read out of the
                // method body. Without this, a definition written the way the documentation recommends looks like a
                // message that declares nothing at all.
                if (describingContract is not null &&
                    declarations is not null &&
                    SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, describingContract) &&
                    contract.TypeArguments.Length == 1 &&
                    contract.TypeArguments[0] is INamedTypeSymbol describedByBody &&
                    DescribeDeclares(type, valueType, declarations, auditDeclaration, compilation, cancellationToken))
                {
                    builder.Add(describedByBody);
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Reads a definition's <c>Describe</c> body to decide whether it declares the required value type.
    /// </summary>
    /// <param name="definition">The definition type implementing the describing contract.</param>
    /// <param name="valueType">The value type a requirement asks every message to declare.</param>
    /// <param name="declarations">The <c>IMessageDeclarations</c> symbol whose calls are the declarations.</param>
    /// <param name="auditDeclaration">The <c>AuditDeclaration</c> symbol, when the audit contracts are referenced.</param>
    /// <param name="compilation">The compilation, for the semantic model of the body.</param>
    /// <param name="cancellationToken">Cancels the analysis.</param>
    /// <returns>
    ///     <see langword="true" /> when the body declares the value type, or when the body cannot be read at all.
    /// </returns>
    /// <remarks>
    ///     Unreadable is treated as declared, deliberately. This analyzer's failure mode matters: a false negative is
    ///     a rule the composition check still enforces at startup, while a false positive is a build that cannot be
    ///     made to pass without turning the rule off. So a body in a referenced assembly, an implementation this pass
    ///     cannot find, and a body that hands the collector to another method are all treated as covering the message.
    /// </remarks>
    private static bool DescribeDeclares(
        INamedTypeSymbol definition,
        INamedTypeSymbol valueType,
        INamedTypeSymbol declarations,
        INamedTypeSymbol? auditDeclaration,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var describe = definition
            .GetMembers(DescribeMethodName)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method => method.Parameters.Length == 1 &&
                                      SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, declarations));

        var reference = describe?.DeclaringSyntaxReferences.FirstOrDefault();

        if (describe is null || reference is null)
        {
            return true;
        }

        var syntax = reference.GetSyntax(cancellationToken);
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var collector = describe.Parameters[0];

        foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (PassesCollector(invocation, model, collector, cancellationToken))
            {
                return true;
            }

            if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol called ||
                called.ContainingType is null ||
                !SymbolEqualityComparer.Default.Equals(called.ContainingType.OriginalDefinition, declarations))
            {
                continue;
            }

            if (DeclaresValueType(called, valueType, auditDeclaration))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether one call on the declaration collector declares the required value type.
    /// </summary>
    /// <param name="called">The collector method the body called.</param>
    /// <param name="valueType">The value type a requirement asks for.</param>
    /// <param name="auditDeclaration">The <c>AuditDeclaration</c> symbol, when the audit contracts are referenced.</param>
    /// <returns><see langword="true" /> when the call declares that value type.</returns>
    private static bool DeclaresValueType(
        IMethodSymbol called,
        INamedTypeSymbol valueType,
        INamedTypeSymbol? auditDeclaration)
    {
        switch (called.Name)
        {
            // Both are keyed by the one type argument. Declare states a value and Exempt states that the message
            // deliberately has none, and a requirement is answered either way, exactly as [DeclarationExempt] is.
            case "Declare":
            case "Exempt":
                return called.TypeArguments.Length == 1 &&
                       SymbolEqualityComparer.Default.Equals(called.TypeArguments[0], valueType);

            // Both key their declaration by AuditDeclaration, which is what makes NotAudited answer the same
            // question Audited does: the message stated its audit position either way.
            case "Audited":
            case "NotAudited":
                return auditDeclaration is not null &&
                       SymbolEqualityComparer.Default.Equals(valueType, auditDeclaration);

            default:
                return false;
        }
    }

    /// <summary>
    ///     Determines whether an invocation hands the declaration collector to something else.
    /// </summary>
    /// <param name="invocation">The invocation in the body.</param>
    /// <param name="model">The semantic model of the body.</param>
    /// <param name="collector">The <c>Describe</c> parameter holding the collector.</param>
    /// <param name="cancellationToken">Cancels the analysis.</param>
    /// <returns><see langword="true" /> when the collector is passed as an argument.</returns>
    /// <remarks>
    ///     A body that calls a shared helper declares through it, out of this pass's sight. Treating that as covered
    ///     keeps the analyzer from failing a build over a declaration that is there.
    /// </remarks>
    private static bool PassesCollector(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        IParameterSymbol collector,
        CancellationToken cancellationToken)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (model.GetSymbolInfo(argument.Expression, cancellationToken).Symbol is IParameterSymbol passed &&
                SymbolEqualityComparer.Default.Equals(passed, collector))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Collects every attribute class annotated as declaring the given value type.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="valueType">The metadata value type in question.</param>
    /// <param name="cancellationToken">The analysis cancellation token.</param>
    /// <returns>The attribute classes that declare the value type.</returns>
    /// <remarks>
    ///     Referenced assemblies are scanned as well as the analyzed one, because LiteBus's own
    ///     <c>[Audited]</c> and <c>[AuditExempt]</c> live in a package and an application's declaring attributes often
    ///     live in a shared project.
    /// </remarks>
    private static ImmutableHashSet<INamedTypeSymbol> CollectDeclaringAttributes(
        Compilation compilation,
        INamedTypeSymbol valueType,
        CancellationToken cancellationToken)
    {
        var annotation = compilation.GetTypeByMetadataName(MessageDeclarationAttributeMetadataName);

        if (annotation is null)
        {
            return ImmutableHashSet.Create<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        }

        var builder = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var module in EnumerateAssemblies(compilation))
        {
            foreach (var type in EnumerateTypes(module.GlobalNamespace, cancellationToken))
            {
                foreach (var attribute in type.GetAttributes())
                {
                    if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, annotation) ||
                        attribute.ConstructorArguments.Length == 0)
                    {
                        continue;
                    }

                    if (attribute.ConstructorArguments[0].Value is INamedTypeSymbol declared &&
                        SymbolEqualityComparer.Default.Equals(declared, valueType))
                    {
                        builder.Add(type);
                    }
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Enumerates the analyzed assembly and every assembly it references.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns>The assemblies to scan for declaring attributes.</returns>
    private static IEnumerable<IAssemblySymbol> EnumerateAssemblies(Compilation compilation)
    {
        yield return compilation.Assembly;

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
            {
                yield return assembly;
            }
        }
    }

    /// <summary>
    ///     Walks a namespace and yields every type it contains, nested types included.
    /// </summary>
    /// <param name="namespaceSymbol">The namespace to walk.</param>
    /// <param name="cancellationToken">The analysis cancellation token.</param>
    /// <returns>Every type reachable from the namespace.</returns>
    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(
        INamespaceSymbol namespaceSymbol,
        CancellationToken cancellationToken)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (member)
            {
                case INamespaceSymbol nested:
                    foreach (var type in EnumerateTypes(nested, cancellationToken))
                    {
                        yield return type;
                    }

                    break;

                case INamedTypeSymbol type:
                    foreach (var nestedType in EnumerateTypeAndNested(type, cancellationToken))
                    {
                        yield return nestedType;
                    }

                    break;
            }
        }
    }

    /// <summary>
    ///     Yields a type and every type nested inside it.
    /// </summary>
    /// <param name="type">The outer type.</param>
    /// <param name="cancellationToken">The analysis cancellation token.</param>
    /// <returns>The type and its nested types.</returns>
    private static IEnumerable<INamedTypeSymbol> EnumerateTypeAndNested(
        INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        yield return type;

        foreach (var nested in type.GetTypeMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var inner in EnumerateTypeAndNested(nested, cancellationToken))
            {
                yield return inner;
            }
        }
    }
}
