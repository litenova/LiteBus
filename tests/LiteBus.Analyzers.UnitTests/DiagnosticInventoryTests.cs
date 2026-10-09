using System.Reflection;
using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests that every analyzer rule identifier has a descriptor.
/// </summary>
public sealed class DiagnosticInventoryTests
{
    /// <summary>
    ///     Verifies every <c>DiagnosticIds</c> constant has a matching descriptor.
    /// </summary>
    [Fact]
    public void EveryDiagnosticId_HasDescriptor()
    {
        var ids = typeof(DiagnosticIds).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        var descriptorIds = typeof(DiagnosticDescriptors).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => ((DiagnosticDescriptor)field.GetValue(null)!).Id)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(ids);

        foreach (var id in ids)
        {
            Assert.Contains(id, descriptorIds);
        }
    }
}
