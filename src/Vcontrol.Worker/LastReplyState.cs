namespace Vcontrol.Worker;

internal sealed class LastReplyState
{
    public const int MaxConsecutiveReadFailures = 3;

    private readonly Lock _lock = new();

    public bool HasReported { get; private set; }
    public bool LastSuccess { get; private set; }
    public bool IsHealthy { get; private set; } = true;
    public int ConsecutiveReadFailures { get; private set; }
    public DateTimeOffset? LastSuccessAt { get; private set; }
    public DateTimeOffset? LastFailureAt { get; private set; }
    public int? LastExitCode { get; private set; }
    public string? LastError { get; private set; }

    public void ReportSuccess(int exitCode, string? error)
    {
        lock (_lock)
        {
            HasReported = true;
            LastSuccess = true;
            IsHealthy = true;
            ConsecutiveReadFailures = 0;
            LastExitCode = exitCode;
            LastSuccessAt = DateTimeOffset.UtcNow;
            // Keep the last error around for diagnostics, even on success,
            // but clear it if not provided.
            LastError = string.IsNullOrWhiteSpace(error) ? null : error;
        }
    }

    public void ReportFailure(int exitCode, string? error)
    {
        lock (_lock)
        {
            HasReported = true;
            LastSuccess = false;
            IsHealthy = false;
            ConsecutiveReadFailures = 0;
            LastExitCode = exitCode;
            LastFailureAt = DateTimeOffset.UtcNow;
            LastError = error;
        }
    }

    public void ReportTransientReadFailure(int exitCode, string? error)
    {
        lock (_lock)
        {
            HasReported = true;
            LastSuccess = false;
            ConsecutiveReadFailures++;
            IsHealthy = ConsecutiveReadFailures < MaxConsecutiveReadFailures;
            LastExitCode = exitCode;
            LastFailureAt = DateTimeOffset.UtcNow;
            LastError = error;
        }
    }

    public LastReplySnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new LastReplySnapshot(
                HasReported,
                LastSuccess,
                IsHealthy,
                ConsecutiveReadFailures,
                LastSuccessAt,
                LastFailureAt,
                LastExitCode,
                LastError
            );
        }
    }
}

internal sealed record LastReplySnapshot(
    bool HasReported,
    bool LastSuccess,
    bool IsHealthy,
    int ConsecutiveReadFailures,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    int? LastExitCode,
    string? LastError
);
