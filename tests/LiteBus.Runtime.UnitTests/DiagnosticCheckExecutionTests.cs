using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Runtime.UnitTests;

public sealed class DiagnosticCheckExecutionTests
{
    [Fact]
    public async Task CheckAsync_WhenProbeNameDiffersFromDescriptor_ShouldThrowDiagnosticCheckNameMismatchException()
    {
        var descriptor = new DiagnosticCheckDescriptor(typeof(MismatchedDiagnosticCheck), "expected.name");
        var check = new MismatchedDiagnosticCheck();

        var act = () => DiagnosticCheckExecution.CheckAsync(descriptor, check);

        await act.Should().ThrowAsync<DiagnosticCheckNameMismatchException>();
    }

    private sealed class MismatchedDiagnosticCheck : IDiagnosticCheck
    {
        public string Name => "actual.name";

        public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new DiagnosticResult(DiagnosticStatus.Healthy, "ok"));
    }
}
