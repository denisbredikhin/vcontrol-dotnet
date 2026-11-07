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

    [Fact]
    public void ReportTransientReadFailure_keeps_health_until_the_third_consecutive_failure()
    {
        // Arrange
        var sut = new LastReplyState();

        // Act
        sut.ReportTransientReadFailure(-1, "read failed");
        sut.ReportTransientReadFailure(-1, "read failed");

        // Assert
        var snapshot = sut.GetSnapshot();
        snapshot.LastSuccess.Should().BeFalse();
        snapshot.IsHealthy.Should().BeTrue();
        snapshot.ConsecutiveReadFailures.Should().Be(2);
    }

    [Fact]
    public void ReportTransientReadFailure_marks_health_unhealthy_on_the_third_consecutive_failure()
    {
        // Arrange
        var sut = new LastReplyState();

        // Act
        sut.ReportTransientReadFailure(-1, "read failed");
        sut.ReportTransientReadFailure(-1, "read failed");
        sut.ReportTransientReadFailure(-1, "read failed");

        // Assert
        var snapshot = sut.GetSnapshot();
        snapshot.IsHealthy.Should().BeFalse();
        snapshot.ConsecutiveReadFailures.Should().Be(3);
    }

    [Fact]
    public void ReportSuccess_resets_consecutive_transient_read_failures()
    {
        // Arrange
        var sut = new LastReplyState();
        sut.ReportTransientReadFailure(-1, "read failed");
        sut.ReportTransientReadFailure(-1, "read failed");

        // Act
        sut.ReportSuccess(0, null);

        // Assert
        var snapshot = sut.GetSnapshot();
        snapshot.IsHealthy.Should().BeTrue();
        snapshot.LastSuccess.Should().BeTrue();
        snapshot.ConsecutiveReadFailures.Should().Be(0);
    }
}
