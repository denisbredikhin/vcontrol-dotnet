namespace Vcontrol.Worker;

public sealed class VclientQueryResult
{
    public IReadOnlyList<VclientReading> Readings { get; init; } = [];
    public string Stderr { get; init; } = string.Empty;
    public int ExitCode { get; init; }
}
