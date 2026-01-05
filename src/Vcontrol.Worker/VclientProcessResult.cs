namespace Vcontrol.Worker;

public sealed class VclientProcessResult
{
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
    public int ExitCode { get; init; }
}
