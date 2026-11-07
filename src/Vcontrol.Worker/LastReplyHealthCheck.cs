using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Vcontrol.Worker;

internal sealed class LastReplyHealthCheck(LastReplyState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var snapshot = state.GetSnapshot();

        if (!snapshot.HasReported)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "No replies have been recorded yet.",
                data: new Dictionary<string, object>
                {
                    ["hasReported"] = snapshot.HasReported,
                    ["lastSuccess"] = snapshot.LastSuccess,
                    ["consecutiveReadFailures"] = snapshot.ConsecutiveReadFailures,
                    ["lastSuccessAt"] = snapshot.LastSuccessAt?.ToString("O") ?? string.Empty,
                    ["lastFailureAt"] = snapshot.LastFailureAt?.ToString("O") ?? string.Empty,
                    ["lastExitCode"] = snapshot.LastExitCode ?? -1,
                    ["lastError"] = snapshot.LastError ?? string.Empty
                }));
        }

        var data = new Dictionary<string, object>
        {
            ["hasReported"] = snapshot.HasReported,
            ["lastSuccess"] = snapshot.LastSuccess,
            ["consecutiveReadFailures"] = snapshot.ConsecutiveReadFailures,
            ["lastSuccessAt"] = snapshot.LastSuccessAt?.ToString("O") ?? string.Empty,
            ["lastFailureAt"] = snapshot.LastFailureAt?.ToString("O") ?? string.Empty,
            ["lastExitCode"] = snapshot.LastExitCode ?? -1,
            ["lastError"] = snapshot.LastError ?? string.Empty
        };

        if (snapshot.IsHealthy)
        {
            var description = snapshot.LastSuccess
                ? "Last client reply was successful."
                : $"Transient read failure {snapshot.ConsecutiveReadFailures} of {LastReplyState.MaxConsecutiveReadFailures}; readiness remains healthy.";
            return Task.FromResult(HealthCheckResult.Healthy(description, data));
        }

        return Task.FromResult(HealthCheckResult.Degraded("Last client reply failed.", data: data));
    }
}
