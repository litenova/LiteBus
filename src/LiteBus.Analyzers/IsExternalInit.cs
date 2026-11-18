// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;

/// <summary>
///     Lets the analyzer, which targets netstandard2.0, declare records and init-only properties; the compiler requires
///     this marker type and netstandard2.0 does not ship it.
/// </summary>
internal static class IsExternalInit
{
}
