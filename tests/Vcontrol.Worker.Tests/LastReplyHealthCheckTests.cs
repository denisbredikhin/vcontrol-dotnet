using AwesomeAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Vcontrol.Worker.Tests;

public sealed class LastReplyHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_returns_healthy_when_no_replies_were_reported()
    {
        // Arrange
        var state = new LastReplyState();
        var sut = new LastReplyHealthCheck(state);
        var context = new HealthCheckContext();

        // Act
        var result = await sut.CheckHealthAsync(context, TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("No replies have been recorded yet.");
        result.Data["hasReported"].Should().Be(false);
        result.Data["lastSuccess"].Should().Be(false);
        result.Data["lastExitCode"].Should().Be(-1);
        result.Data["lastError"].Should().Be(string.Empty);
    }

    [Fact]
    public async Task CheckHealthAsync_returns_healthy_when_last_reply_succeeded()
    {
        // Arrange
        var state = new LastReplyState();
        state.ReportSuccess(0, null);
        var sut = new LastReplyHealthCheck(state);
        var context = new HealthCheckContext();

        // Act
        var result = await sut.CheckHealthAsync(context, TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Last client reply was successful.");
        result.Data["hasReported"].Should().Be(true);
        result.Data["lastSuccess"].Should().Be(true);
        result.Data["lastExitCode"].Should().Be(0);
        result.Data["lastFailureAt"].Should().Be(string.Empty);
    }

    [Fact]
    public async Task CheckHealthAsync_returns_degraded_when_last_reply_failed()
    {
        // Arrange
        var state = new LastReplyState();
        state.ReportFailure(2, "failure");
        var sut = new LastReplyHealthCheck(state);
        var context = new HealthCheckContext();

        // Act
        var result = await sut.CheckHealthAsync(context, TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be("Last client reply failed.");
        result.Data["hasReported"].Should().Be(true);
        result.Data["lastSuccess"].Should().Be(false);
        result.Data["lastExitCode"].Should().Be(2);
        result.Data["lastError"].Should().Be("failure");
    }
}
