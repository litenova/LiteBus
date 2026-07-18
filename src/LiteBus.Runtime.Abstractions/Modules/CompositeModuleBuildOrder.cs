namespace LiteBus.Runtime.Abstractions;

/// <summary>
///     Specifies the build relationship between a composite module and the children it declares.
/// </summary>
public enum CompositeModuleBuildOrder
{
    /// <summary>
    ///     Builds the composite parent before its declared children.
    /// </summary>
    ParentFirst,

    /// <summary>
    ///     Builds every declared child before the composite parent.
    /// </summary>
    ChildrenFirst = 1
}
