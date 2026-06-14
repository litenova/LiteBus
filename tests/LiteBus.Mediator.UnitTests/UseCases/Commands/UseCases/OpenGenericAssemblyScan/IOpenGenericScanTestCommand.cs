namespace LiteBus.Mediator.UnitTests.UseCases.Commands.UseCases.OpenGenericAssemblyScan;

/// <summary>
///     Marker interface for commands used in open generic assembly scan tests.
///     This constrains the open generic handlers so they only apply to these test commands
///     and do not affect existing tests.
/// </summary>
public interface IOpenGenericScanTestCommand : IAuditableCommand;