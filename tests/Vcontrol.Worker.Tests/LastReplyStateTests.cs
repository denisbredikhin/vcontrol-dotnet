using AwesomeAssertions;
using Vcontrol.Worker;

namespace Vcontrol.Worker.Tests;

public sealed class LastReplyStateTests
{
    [Fact]
    public void ReportSuccess_records_success_snapshot_and_clears_blank_error()
    {
        // Arrange
        var sut = new LastReplyState();

        // Act
        sut.ReportSuccess(0, "   ");

        // Assert
        var snapshot = sut.GetSnapshot();
        snapshot.HasReported.Should().BeTrue();
        snapshot.LastSuccess.Should().BeTrue();
        snapshot.LastExitCode.Should().Be(0);
        snapshot.LastSuccessAt.Should().NotBeNull();
        snapshot.LastError.Should().BeNull();
    }

    [Fact]
    public void ReportSuccess_records_success_snapshot_and_keeps_non_blank_error()
    {
        // Arrange
        var sut = new LastReplyState();

        // Act
        sut.ReportSuccess(0, "warning");

        // Assert
        var snapshot = sut.GetSnapshot();
        snapshot.HasReported.Should().BeTrue();
        snapshot.LastSuccess.Should().BeTrue();
        snapshot.LastExitCode.Should().Be(0);
        snapshot.LastSuccessAt.Should().NotBeNull();
        snapshot.LastError.Should().Be("warning");
    }

    [Fact]
    public void ReportFailure_records_failure_snapshot_and_error()
    {
        // Arrange
        var sut = new LastReplyState();

        // Act
        sut.ReportFailure(7, "boom");

        // Assert
        var snapshot = sut.GetSnapshot();
        snapshot.HasReported.Should().BeTrue();
        snapshot.LastSuccess.Should().BeFalse();
        snapshot.LastExitCode.Should().Be(7);
        snapshot.LastFailureAt.Should().NotBeNull();
        snapshot.LastError.Should().Be("boom");
    }
}
