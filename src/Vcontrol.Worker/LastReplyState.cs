using System;

namespace Vcontrol.Worker;

public sealed class LastReplyState
{
    private readonly object _lock = new();

    public bool HasReported { get; private set; }
    public bool LastSuccess { get; private set; }
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
                LastSuccessAt,
                LastFailureAt,
                LastExitCode,
                LastError
            );
        }
    }
}

public sealed record LastReplySnapshot(
    bool HasReported,
    bool LastSuccess,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    int? LastExitCode,
    string? LastError
);

