using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Shared helpers for durable contract registration discovery.
/// </summary>
internal static class ContractRegistrationAnalysis
{
    /// <summary>
    ///     Collects message types registered through explicit <c>Contracts.Register</c> calls.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The registered message type symbols.</returns>
    /// <remarks>
    ///     <c>Register</c> on a contract writer surface counts, as does <c>Register</c> on any other scan-capable
    ///     surface. <c>Register</c> on the message and command module builders only records handler and message
    ///     types and never satisfies durable contract registration.
    /// </remarks>
    internal static ImmutableHashSet<ITypeSymbol> CollectRegisteredContractTypes(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var builder = ImmutableHashSet.CreateBuilder<ITypeSymbol>(SymbolEqualityComparer.Default);
        var contractWriter = compilation.GetTypeByMetadataName("LiteBus.Messaging.Abstractions.IContractWriter");
        var contractRegistry = compilation.GetTypeByMetadataName("LiteBus.Messaging.Abstractions.IMessageContractRegistry");
        var contractBuilder = compilation.GetTypeByMetadataName("LiteBus.Messaging.Abstractions.MessageContractBuilder");
        var messageModuleBuilder = compilation.GetTypeByMetadataName("LiteBus.Messaging.MessageModuleBuilder");
        var commandModuleBuilder = compilation.GetTypeByMetadataName("LiteBus.Commands.CommandModuleBuilder");

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(syntaxTree);

            foreach (var invocation in syntaxTree.GetRoot(cancellationToken).DescendantNodes()
                         .OfType<InvocationExpressionSyntax>())
            {
                var method = model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;

                if (method is null ||
                    !IsContractRegisterMethod(
                        method,
                        contractWriter,
                        contractRegistry,
                        contractBuilder,
                        messageModuleBuilder,
                        commandModuleBuilder))
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

                var typeArgument = ResolveTypeArgument(
                    invocation.ArgumentList.Arguments[0].Expression,
                    model,
                    cancellationToken);

                if (typeArgument is not null)
                {
                    builder.Add(typeArgument);
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Collects assemblies passed to <c>RegisterFromAssembly</c> or <c>AddFromAssembly</c> calls.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The assemblies registered through assembly scanning.</returns>
    /// <remarks>
    ///     Every assembly scan counts except <c>CommandModuleBuilder.RegisterFromAssembly</c>, which only records
    ///     message types. <c>MessageModuleBuilder.RegisterFromAssembly</c> applies scanned contracts and counts.
    /// </remarks>
    internal static ImmutableHashSet<IAssemblySymbol> CollectRegisterFromAssemblyTargets(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var builder = ImmutableHashSet.CreateBuilder<IAssemblySymbol>(SymbolEqualityComparer.Default);
        var contractWriter = compilation.GetTypeByMetadataName("LiteBus.Messaging.Abstractions.IContractWriter");
        var contractRegistry = compilation.GetTypeByMetadataName("LiteBus.Messaging.Abstractions.IMessageContractRegistry");
        var contractBuilder = compilation.GetTypeByMetadataName("LiteBus.Messaging.Abstractions.MessageContractBuilder");
        var commandModuleBuilder = compilation.GetTypeByMetadataName("LiteBus.Commands.CommandModuleBuilder");

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(syntaxTree);

            foreach (var invocation in syntaxTree.GetRoot(cancellationToken).DescendantNodes()
                         .OfType<InvocationExpressionSyntax>())
            {
                var method = model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;

                if (method is null ||
                    !IsContractRegisterFromAssemblyMethod(
                        method,
                        contractWriter,
                        contractRegistry,
                        contractBuilder,
                        commandModuleBuilder))
                {
                    continue;
                }

                if (invocation.ArgumentList.Arguments.Count == 0)
                {
                    continue;
                }

                var assemblyArgument = invocation.ArgumentList.Arguments[0].Expression;
                var assemblySymbol = ResolveAssemblyArgument(assemblyArgument, model, cancellationToken);

                if (assemblySymbol is not null)
                {
                    builder.Add(assemblySymbol);
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Determines whether the message type is covered by explicit registration or assembly scanning.
    /// </summary>
    /// <param name="messageType">The message type symbol.</param>
    /// <param name="registeredTypes">Types registered through <c>Register</c>.</param>
    /// <param name="registeredAssemblies">Assemblies registered through <c>RegisterFromAssembly</c>.</param>
    /// <returns><see langword="true" /> when the type is explicitly registered.</returns>
    internal static bool IsExplicitlyRegistered(
        ITypeSymbol messageType,
        ImmutableHashSet<ITypeSymbol> registeredTypes,
        ImmutableHashSet<IAssemblySymbol> registeredAssemblies)
    {
        if (registeredTypes.Contains(messageType, SymbolEqualityComparer.Default))
        {
            return true;
        }

        return registeredAssemblies.Contains(messageType.ContainingAssembly, SymbolEqualityComparer.Default);
    }

    /// <summary>
    ///     Gets the closed registration type display string for diagnostics.
    /// </summary>
    /// <param name="messageType">The message type symbol.</param>
    /// <returns>The closed type display string.</returns>
    internal static string GetClosedRegistrationTypeDisplay(ITypeSymbol messageType)
    {
        if (messageType is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: > 0 } namedType &&
            !HandlerAnalysis.IsGenericTypeDefinition(namedType))
        {
            return namedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        }

        return messageType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }

    /// <summary>
    ///     Determines whether the type declares <c>[MessageContract]</c>.
    /// </summary>
    /// <param name="messageType">The message type symbol.</param>
    /// <returns><see langword="true" /> when the attribute is present; otherwise, <see langword="false" />.</returns>
    internal static bool HasMessageContractAttribute(ITypeSymbol messageType)
    {
        if (messageType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        foreach (var attribute in namedType.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "MessageContractAttribute" &&
                attribute.AttributeClass.ContainingNamespace?.ToDisplayString() is
                    "LiteBus.Messaging.Abstractions" or "LiteBus.Analyzers")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether the type is a durable command or event message.
    /// </summary>
    /// <param name="messageType">The message type symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when the type implements a durable message contract.</returns>
    internal static bool IsDurableMessageType(ITypeSymbol messageType, Compilation compilation)
    {
        var command = compilation.GetTypeByMetadataName("LiteBus.Commands.Abstractions.ICommand");
        var commandWithResult = compilation.GetTypeByMetadataName("LiteBus.Commands.Abstractions.ICommand`1");
        var eventType = compilation.GetTypeByMetadataName("LiteBus.Events.Abstractions.IEvent");

        foreach (var candidate in messageType.AllInterfaces)
        {
            var original = candidate.OriginalDefinition;

            if (command is not null && SymbolEqualityComparer.Default.Equals(original, command))
            {
                return true;
            }

            if (commandWithResult is not null && SymbolEqualityComparer.Default.Equals(original, commandWithResult))
            {
                return true;
            }

            if (eventType is not null && SymbolEqualityComparer.Default.Equals(original, eventType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Resolves the type argument from a registration expression.
    /// </summary>
    /// <param name="expression">The registration argument expression.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resolved type symbol, if any.</returns>
    private static ITypeSymbol? ResolveTypeArgument(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (expression is TypeOfExpressionSyntax typeOfExpression)
        {
            return semanticModel.GetTypeInfo(typeOfExpression.Type, cancellationToken).Type;
        }

        return semanticModel.GetTypeInfo(expression, cancellationToken).Type;
    }

    /// <summary>
    ///     Resolves the assembly argument from a registration expression.
    /// </summary>
    /// <param name="expression">The assembly argument expression.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resolved assembly symbol, if any.</returns>
    /// <remarks>
    ///     Only <c>typeof(T)</c> and <c>typeof(T).Assembly</c> forms resolve. Assembly instances observed at analysis
    ///     time are never read from disk.
    /// </remarks>
    private static IAssemblySymbol? ResolveAssemblyArgument(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (expression is TypeOfExpressionSyntax typeOfExpression)
        {
            var type = semanticModel.GetTypeInfo(typeOfExpression.Type, cancellationToken).Type;

            return type?.ContainingAssembly;
        }

        var memberAccess = expression as MemberAccessExpressionSyntax;

        if (memberAccess?.Name.Identifier.Text == "Assembly" &&
            memberAccess.Expression is TypeOfExpressionSyntax typeofExpression)
        {
            var type = semanticModel.GetTypeInfo(typeofExpression.Type, cancellationToken).Type;

            return type?.ContainingAssembly;
        }

        return null;
    }

    /// <summary>
    ///     Determines whether the method registers a durable contract type.
    /// </summary>
    /// <param name="method">The invoked method symbol.</param>
    /// <param name="contractWriter">The <c>IContractWriter</c> symbol.</param>
    /// <param name="contractRegistry">The <c>IMessageContractRegistry</c> symbol.</param>
    /// <param name="contractBuilder">The <c>MessageContractBuilder</c> symbol.</param>
    /// <param name="messageModuleBuilder">The <c>MessageModuleBuilder</c> symbol.</param>
    /// <param name="commandModuleBuilder">The <c>CommandModuleBuilder</c> symbol.</param>
    /// <returns><see langword="true" /> when the method registers a contract.</returns>
    /// <remarks>
    ///     <c>Register</c> on a contract writer surface always counts. <c>Register</c> on any other type counts
    ///     when that type also offers an assembly scan, which distinguishes contract registries from unrelated
    ///     <c>Register</c> methods. The message and command module builders are excluded: their <c>Register</c>
    ///     only records handler and message types.
    /// </remarks>
    private static bool IsContractRegisterMethod(
        IMethodSymbol method,
        INamedTypeSymbol? contractWriter,
        INamedTypeSymbol? contractRegistry,
        INamedTypeSymbol? contractBuilder,
        INamedTypeSymbol? messageModuleBuilder,
        INamedTypeSymbol? commandModuleBuilder)
    {
        if (method.Name != "Register" || method.ContainingType is null)
        {
            return false;
        }

        if (!HasContractRegisterSignature(method))
        {
            return false;
        }

        if (IsContractWriterType(method.ContainingType, contractWriter, contractRegistry, contractBuilder))
        {
            return true;
        }

        if (MatchesTypeDefinition(method.ContainingType, messageModuleBuilder) ||
            MatchesTypeDefinition(method.ContainingType, commandModuleBuilder))
        {
            return false;
        }

        return HasAssemblyScanMember(method.ContainingType);
    }

    /// <summary>
    ///     Determines whether the method has a contract registration signature.
    /// </summary>
    /// <param name="method">The invoked method symbol.</param>
    /// <returns><see langword="true" /> when the method is generic or takes a <c>System.Type</c> first.</returns>
    private static bool HasContractRegisterSignature(IMethodSymbol method)
    {
        if (method.TypeArguments.Length > 0)
        {
            return true;
        }

        var firstParameter = method.Parameters.FirstOrDefault();

        return firstParameter is not null &&
               firstParameter.Type.Name == "Type" &&
               firstParameter.Type.ContainingNamespace?.ToDisplayString() == "System";
    }

    /// <summary>
    ///     Determines whether the type offers an assembly scan member.
    /// </summary>
    /// <param name="containingType">The invoked method containing type.</param>
    /// <returns><see langword="true" /> when the type declares or inherits a scan member.</returns>
    private static bool HasAssemblyScanMember(INamedTypeSymbol containingType)
    {
        return !containingType.GetMembers("RegisterFromAssembly").IsEmpty ||
               !containingType.GetMembers("AddFromAssembly").IsEmpty;
    }

    /// <summary>
    ///     Determines whether the type is a durable contract writer surface.
    /// </summary>
    /// <param name="containingType">The invoked method containing type.</param>
    /// <param name="contractWriter">The <c>IContractWriter</c> symbol.</param>
    /// <param name="contractRegistry">The <c>IMessageContractRegistry</c> symbol.</param>
    /// <param name="contractBuilder">The <c>MessageContractBuilder</c> symbol.</param>
    /// <returns><see langword="true" /> when the type is, or implements, a contract writer surface.</returns>
    private static bool IsContractWriterType(
        INamedTypeSymbol containingType,
        INamedTypeSymbol? contractWriter,
        INamedTypeSymbol? contractRegistry,
        INamedTypeSymbol? contractBuilder)
    {
        if (MatchesTypeDefinition(containingType, contractWriter) ||
            MatchesTypeDefinition(containingType, contractRegistry) ||
            MatchesTypeDefinition(containingType, contractBuilder))
        {
            return true;
        }

        foreach (var interfaceType in containingType.AllInterfaces)
        {
            if (MatchesTypeDefinition(interfaceType, contractWriter) ||
                MatchesTypeDefinition(interfaceType, contractRegistry))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether the candidate type matches the expected type definition.
    /// </summary>
    /// <param name="candidate">The candidate type symbol.</param>
    /// <param name="expected">The expected type symbol, if resolved.</param>
    /// <returns><see langword="true" /> when the original definitions match; otherwise, <see langword="false" />.</returns>
    private static bool MatchesTypeDefinition(INamedTypeSymbol candidate, INamedTypeSymbol? expected)
    {
        if (expected is null)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, expected);
    }

    /// <summary>
    ///     Determines whether the method registers contracts from an assembly scan.
    /// </summary>
    /// <param name="method">The invoked method symbol.</param>
    /// <param name="contractWriter">The <c>IContractWriter</c> symbol.</param>
    /// <param name="contractRegistry">The <c>IMessageContractRegistry</c> symbol.</param>
    /// <param name="contractBuilder">The <c>MessageContractBuilder</c> symbol.</param>
    /// <param name="commandModuleBuilder">The <c>CommandModuleBuilder</c> symbol.</param>
    /// <returns><see langword="true" /> when the method scans an assembly.</returns>
    /// <remarks>
    ///     <c>AddFromAssembly</c> counts on contract writer surfaces. <c>RegisterFromAssembly</c> counts on every
    ///     surface, including <c>MessageModuleBuilder</c> and the <c>IContractWriter</c> scan extension, except
    ///     <c>CommandModuleBuilder</c>, whose scan only records message types.
    /// </remarks>
    private static bool IsContractRegisterFromAssemblyMethod(
        IMethodSymbol method,
        INamedTypeSymbol? contractWriter,
        INamedTypeSymbol? contractRegistry,
        INamedTypeSymbol? contractBuilder,
        INamedTypeSymbol? commandModuleBuilder)
    {
        if (method.ContainingType is null)
        {
            return false;
        }

        if (method.Name == "AddFromAssembly")
        {
            return IsContractWriterType(method.ContainingType, contractWriter, contractRegistry, contractBuilder);
        }

        if (method.Name != "RegisterFromAssembly")
        {
            return false;
        }

        return !MatchesTypeDefinition(method.ContainingType, commandModuleBuilder);
    }
}
