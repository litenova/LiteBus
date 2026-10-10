using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Gathers durable contract registrations from invocation nodes for one compilation.
/// </summary>
/// <remarks>
///     One instance serves one compilation. Syntax node actions feed invocations through <see cref="AddInvocation" />,
///     which rejects invocations whose name cannot register contracts from syntax alone and binds only the rest.
///     <see cref="ToRegisteredTypes" /> and <see cref="ToRegisteredAssemblies" /> run at compilation end.
/// </remarks>
internal sealed class ContractRegistrationCollector
{
    /// <summary>
    ///     The <c>IContractWriter</c> symbol, when the compilation references it.
    /// </summary>
    private readonly INamedTypeSymbol? _contractWriter;

    /// <summary>
    ///     The <c>IMessageContractRegistry</c> symbol, when the compilation references it.
    /// </summary>
    private readonly INamedTypeSymbol? _contractRegistry;

    /// <summary>
    ///     The <c>MessageContractBuilder</c> symbol, when the compilation references it.
    /// </summary>
    private readonly INamedTypeSymbol? _contractBuilder;

    /// <summary>
    ///     The <c>MessageModuleBuilder</c> symbol, when the compilation references it.
    /// </summary>
    private readonly INamedTypeSymbol? _messageModuleBuilder;

    /// <summary>
    ///     The <c>CommandModuleBuilder</c> symbol, when the compilation references it.
    /// </summary>
    private readonly INamedTypeSymbol? _commandModuleBuilder;

    /// <summary>
    ///     The message types registered through <c>Register</c> calls.
    /// </summary>
    private readonly ConcurrentDictionary<ITypeSymbol, byte> _registeredTypes = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     The assemblies registered through <c>RegisterFromAssembly</c> or <c>AddFromAssembly</c> calls.
    /// </summary>
    private readonly ConcurrentDictionary<IAssemblySymbol, byte> _registeredAssemblies = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     Initializes a new instance of the <see cref="ContractRegistrationCollector" /> class.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    internal ContractRegistrationCollector(Compilation compilation)
    {
        _contractWriter = compilation.GetTypeByMetadataName("LiteBus.Messaging.Abstractions.IContractWriter");
        _contractRegistry = compilation.GetTypeByMetadataName("LiteBus.Messaging.Abstractions.IMessageContractRegistry");
        _contractBuilder = compilation.GetTypeByMetadataName("LiteBus.Messaging.Abstractions.MessageContractBuilder");
        _messageModuleBuilder = compilation.GetTypeByMetadataName("LiteBus.Messaging.MessageModuleBuilder");
        _commandModuleBuilder = compilation.GetTypeByMetadataName("LiteBus.Commands.CommandModuleBuilder");
    }

    /// <summary>
    ///     Adds the contract registration performed by one invocation node, when it registers contracts.
    /// </summary>
    /// <param name="semanticModel">The semantic model for the invocation.</param>
    /// <param name="invocation">The invocation node.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    internal void AddInvocation(SemanticModel semanticModel, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var name = GetInvokedName(invocation.Expression);

        if (name == "Register")
        {
            AddRegisteredType(semanticModel, invocation, cancellationToken);
        }
        else if (name == "RegisterFromAssembly" || name == "AddFromAssembly")
        {
            AddRegisteredAssembly(semanticModel, invocation, cancellationToken);
        }
    }

    /// <summary>
    ///     Gets the message types registered through explicit <c>Contracts.Register</c> calls.
    /// </summary>
    /// <returns>The registered message type symbols.</returns>
    internal ImmutableHashSet<ITypeSymbol> ToRegisteredTypes()
    {
        var builder = ImmutableHashSet.CreateBuilder<ITypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var type in _registeredTypes.Keys)
        {
            builder.Add(type);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Gets the assemblies passed to <c>RegisterFromAssembly</c> or <c>AddFromAssembly</c> calls.
    /// </summary>
    /// <returns>The assemblies registered through assembly scanning.</returns>
    internal ImmutableHashSet<IAssemblySymbol> ToRegisteredAssemblies()
    {
        var builder = ImmutableHashSet.CreateBuilder<IAssemblySymbol>(SymbolEqualityComparer.Default);

        foreach (var assembly in _registeredAssemblies.Keys)
        {
            builder.Add(assembly);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Gets the invoked member name without binding, unwrapping redundant syntax.
    /// </summary>
    /// <param name="expression">The invoked expression.</param>
    /// <returns>The member name, or <see langword="null" /> when it cannot name a registration.</returns>
    private static string? GetInvokedName(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => null
        };
    }

    /// <summary>
    ///     Adds the message type registered by one <c>Register</c> invocation, when it registers contracts.
    /// </summary>
    /// <param name="semanticModel">The semantic model for the invocation.</param>
    /// <param name="invocation">The invocation node.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    ///     <c>Register</c> on a contract writer surface counts, as does <c>Register</c> on any other scan-capable
    ///     surface. <c>Register</c> on the message and command module builders only records handler and message
    ///     types and never satisfies durable contract registration.
    /// </remarks>
    private void AddRegisteredType(SemanticModel semanticModel, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method ||
            !IsContractRegisterMethod(method))
        {
            return;
        }

        if (method.TypeArguments.Length > 0)
        {
            _registeredTypes.TryAdd(method.TypeArguments[0], 0);
            return;
        }

        if (invocation.ArgumentList.Arguments.Count == 0)
        {
            return;
        }

        var typeArgument = ResolveTypeArgument(
            invocation.ArgumentList.Arguments[0].Expression,
            semanticModel,
            cancellationToken);

        if (typeArgument is not null)
        {
            _registeredTypes.TryAdd(typeArgument, 0);
        }
    }

    /// <summary>
    ///     Adds the assembly registered by one assembly scan invocation, when it scans contracts.
    /// </summary>
    /// <param name="semanticModel">The semantic model for the invocation.</param>
    /// <param name="invocation">The invocation node.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    ///     Every assembly scan counts except <c>CommandModuleBuilder.RegisterFromAssembly</c>, which only records
    ///     message types. <c>MessageModuleBuilder.RegisterFromAssembly</c> applies scanned contracts and counts.
    /// </remarks>
    private void AddRegisteredAssembly(SemanticModel semanticModel, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method ||
            !IsContractRegisterFromAssemblyMethod(method))
        {
            return;
        }

        if (invocation.ArgumentList.Arguments.Count == 0)
        {
            return;
        }

        var assemblyArgument = invocation.ArgumentList.Arguments[0].Expression;
        var assemblySymbol = ResolveAssemblyArgument(assemblyArgument, semanticModel, cancellationToken);

        if (assemblySymbol is not null)
        {
            _registeredAssemblies.TryAdd(assemblySymbol, 0);
        }
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
    /// <returns><see langword="true" /> when the method registers a contract.</returns>
    /// <remarks>
    ///     <c>Register</c> on a contract writer surface always counts. <c>Register</c> on any other type counts
    ///     when that type also offers an assembly scan, which distinguishes contract registries from unrelated
    ///     <c>Register</c> methods. The message and command module builders are excluded: their <c>Register</c>
    ///     only records handler and message types.
    /// </remarks>
    private bool IsContractRegisterMethod(IMethodSymbol method)
    {
        if (method.Name != "Register" || method.ContainingType is null)
        {
            return false;
        }

        if (!HasContractRegisterSignature(method))
        {
            return false;
        }

        if (IsContractWriterType(method.ContainingType))
        {
            return true;
        }

        if (MatchesTypeDefinition(method.ContainingType, _messageModuleBuilder) ||
            MatchesTypeDefinition(method.ContainingType, _commandModuleBuilder))
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
    /// <returns><see langword="true" /> when the type is, or implements, a contract writer surface.</returns>
    private bool IsContractWriterType(INamedTypeSymbol containingType)
    {
        if (MatchesTypeDefinition(containingType, _contractWriter) ||
            MatchesTypeDefinition(containingType, _contractRegistry) ||
            MatchesTypeDefinition(containingType, _contractBuilder))
        {
            return true;
        }

        foreach (var interfaceType in containingType.AllInterfaces)
        {
            if (MatchesTypeDefinition(interfaceType, _contractWriter) ||
                MatchesTypeDefinition(interfaceType, _contractRegistry))
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
    /// <returns><see langword="true" /> when the method scans an assembly.</returns>
    /// <remarks>
    ///     <c>AddFromAssembly</c> counts on contract writer surfaces. <c>RegisterFromAssembly</c> counts on every
    ///     surface, including <c>MessageModuleBuilder</c> and the <c>IContractWriter</c> scan extension, except
    ///     <c>CommandModuleBuilder</c>, whose scan only records message types.
    /// </remarks>
    private bool IsContractRegisterFromAssemblyMethod(IMethodSymbol method)
    {
        if (method.ContainingType is null)
        {
            return false;
        }

        if (method.Name == "AddFromAssembly")
        {
            return IsContractWriterType(method.ContainingType);
        }

        if (method.Name != "RegisterFromAssembly")
        {
            return false;
        }

        return !MatchesTypeDefinition(method.ContainingType, _commandModuleBuilder);
    }
}
