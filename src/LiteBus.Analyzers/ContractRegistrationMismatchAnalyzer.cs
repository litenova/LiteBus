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
///     Reports a contract registration whose name or version differs from the registered type's
///     <c>[MessageContract]</c> attribute.
/// </summary>
/// <remarks>
///     An explicit registration and the attribute must describe the same contract. The runtime rejects a disagreement
///     with <c>MessageContractMismatchException</c> while the host composes, so this rule reports the call and makes
///     the mistake visible at build time. It is a per-call check and needs no compilation-end action.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractRegistrationMismatchAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    ///     The contract version used when <c>[MessageContract]</c> or <c>Register</c> omits the version argument.
    /// </summary>
    private const int DefaultVersion = 1;

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.ContractRegistrationMismatch];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    /// <summary>
    ///     Reports one contract registration whose constant name or version differs from the registered type's
    ///     <c>[MessageContract]</c> attribute.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    /// <remarks>
    ///     The receiver is matched by <see cref="ContractRegistrationCollector" />, the helper the explicit
    ///     registration rule uses, so only contract writer surfaces are checked. A type argument that is not a named
    ///     type, a type without the attribute, and non-constant name or version arguments are skipped.
    /// </remarks>
    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax) context.Node;

        if (context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not IInvocationOperation operation ||
            operation.TargetMethod.Name != "Register" ||
            operation.TargetMethod.TypeArguments.Length == 0 ||
            operation.TargetMethod.TypeArguments[0] is not INamedTypeSymbol messageType)
        {
            return;
        }

        if (!IsContractRegistration(context, invocation, messageType))
        {
            return;
        }

        if (!TryReadRegistrationArguments(operation, out var name, out var version) ||
            !TryReadDeclaredContract(messageType, out var declaredName, out var declaredVersion))
        {
            return;
        }

        if (string.Equals(name, declaredName, StringComparison.Ordinal) && version == declaredVersion)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.ContractRegistrationMismatch,
            invocation.GetLocation(),
            ContractRegistrationAnalysis.GetClosedRegistrationTypeDisplay(messageType),
            name,
            version,
            declaredName,
            declaredVersion));
    }

    /// <summary>
    ///     Determines whether the invocation registers contracts on a contract writer surface.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    /// <param name="invocation">The invocation node.</param>
    /// <param name="messageType">The registered message type symbol.</param>
    /// <returns><see langword="true" /> when the invocation is a contract registration.</returns>
    /// <remarks>
    ///     A fresh collector decides only this invocation, so a nearby <c>Register</c> on an unrelated surface cannot
    ///     make it match. <see cref="ContractRegistrationCollector.AddInvocation" /> performs the receiver matching.
    /// </remarks>
    private static bool IsContractRegistration(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        ITypeSymbol messageType)
    {
        var collector = new ContractRegistrationCollector(context.SemanticModel.Compilation);
        collector.AddInvocation(context.SemanticModel, invocation, context.CancellationToken);

        return collector.ToRegisteredTypes().Contains(messageType);
    }

    /// <summary>
    ///     Reads the constant name and version arguments of a contract registration.
    /// </summary>
    /// <param name="operation">The invocation operation.</param>
    /// <param name="name">The registration name when the method returns <see langword="true" />.</param>
    /// <param name="version">The registration version when the method returns <see langword="true" />.</param>
    /// <returns><see langword="true" /> when both values are compile-time constants.</returns>
    /// <remarks>
    ///     Arguments are matched to parameters by name, so named arguments in a different order resolve as well. An
    ///     omitted version keeps <see cref="DefaultVersion" />, which is also how Roslyn binds the optional parameter.
    /// </remarks>
    private static bool TryReadRegistrationArguments(IInvocationOperation operation, out string name, out int version)
    {
        name = string.Empty;
        version = DefaultVersion;
        var hasName = false;

        foreach (var argument in operation.Arguments)
        {
            switch (argument.Parameter?.Name)
            {
                case "name":
                    if (argument.Value.ConstantValue.HasValue && argument.Value.ConstantValue.Value is string nameValue)
                    {
                        name = nameValue;
                        hasName = true;
                    }
                    else
                    {
                        return false;
                    }

                    break;
                case "version":
                    if (argument.Value.ConstantValue.HasValue && argument.Value.ConstantValue.Value is int versionValue)
                    {
                        version = versionValue;
                    }
                    else if (argument.ArgumentKind == ArgumentKind.DefaultValue &&
                             argument.Parameter?.ExplicitDefaultValue is int defaultVersion)
                    {
                        version = defaultVersion;
                    }
                    else
                    {
                        return false;
                    }

                    break;
            }
        }

        return hasName;
    }

    /// <summary>
    ///     Reads the name and version declared by the message type's <c>[MessageContract]</c> attribute.
    /// </summary>
    /// <param name="messageType">The registered message type symbol.</param>
    /// <param name="name">The declared contract name when the method returns <see langword="true" />.</param>
    /// <param name="version">The declared contract version when the method returns <see langword="true" />.</param>
    /// <returns><see langword="true" /> when the type carries the attribute.</returns>
    private static bool TryReadDeclaredContract(INamedTypeSymbol messageType, out string name, out int version)
    {
        name = string.Empty;
        version = DefaultVersion;

        foreach (var attribute in messageType.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;

            if (attributeClass?.Name != "MessageContractAttribute" ||
                attributeClass.ContainingNamespace?.ToDisplayString() is not
                    ("LiteBus.Messaging.Abstractions" or "LiteBus.Analyzers"))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 0 ||
                attribute.ConstructorArguments[0].Value is not string declaredName)
            {
                return false;
            }

            name = declaredName;
            version = attribute.ConstructorArguments.Length > 1 &&
                      attribute.ConstructorArguments[1].Value is int declaredVersion
                ? declaredVersion
                : DefaultVersion;
            return true;
        }

        return false;
    }
}
