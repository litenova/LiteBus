using System.Collections.Immutable;
using System.Linq;
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
    internal static ImmutableHashSet<ITypeSymbol> CollectRegisteredContractTypes(
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var builder = ImmutableHashSet.CreateBuilder<ITypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(syntaxTree);

            foreach (var invocation in syntaxTree.GetRoot(cancellationToken).DescendantNodes()
                         .OfType<InvocationExpressionSyntax>())
            {
                var method = model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;

                if (method is null || !IsContractRegisterMethod(method))
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
    internal static ImmutableHashSet<IAssemblySymbol> CollectRegisterFromAssemblyTargets(
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var builder = ImmutableHashSet.CreateBuilder<IAssemblySymbol>(SymbolEqualityComparer.Default);

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(syntaxTree);

            foreach (var invocation in syntaxTree.GetRoot(cancellationToken).DescendantNodes()
                         .OfType<InvocationExpressionSyntax>())
            {
                var method = model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;

                if (method is null || !IsContractRegisterFromAssemblyMethod(method))
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
    ///     Determines whether the type declares <see cref="MessageContractAttribute" />.
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
        System.Threading.CancellationToken cancellationToken)
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
    private static IAssemblySymbol? ResolveAssemblyArgument(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
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

        if (semanticModel.GetTypeInfo(expression, cancellationToken).Type is { } typeInfo &&
            typeInfo.Name == "Assembly" &&
            typeInfo.ContainingNamespace?.ToDisplayString() == "System.Reflection")
        {
            var constant = semanticModel.GetConstantValue(expression, cancellationToken);

            if (constant.HasValue && constant.Value is System.Reflection.Assembly assembly)
            {
                return semanticModel.Compilation.GetAssemblyOrModuleSymbol(
                    MetadataReference.CreateFromFile(assembly.Location)) as IAssemblySymbol;
            }
        }

        return null;
    }

    /// <summary>
    ///     Determines whether the method registers a durable contract type.
    /// </summary>
    /// <param name="method">The invoked method symbol.</param>
    /// <returns><see langword="true" /> when the method registers a contract.</returns>
    private static bool IsContractRegisterMethod(IMethodSymbol method)
    {
        if (method.Name != "Register")
        {
            return false;
        }

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
    ///     Determines whether the method registers contracts from an assembly scan.
    /// </summary>
    /// <param name="method">The invoked method symbol.</param>
    /// <returns><see langword="true" /> when the method scans an assembly.</returns>
    private static bool IsContractRegisterFromAssemblyMethod(IMethodSymbol method)
    {
        return method.Name is "RegisterFromAssembly" or "AddFromAssembly";
    }
}
